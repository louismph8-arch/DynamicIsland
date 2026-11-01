using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles;

/// <summary>
/// Grille « bento » de l'île ouverte (refonte O6) : pages de 4 × 2 cases, une seule visible ; en bas, le nom de la tuile survolée
/// (au centre, en fondu) et les points des pages (à droite). Changer de page : clic sur un point, molette (une page par cran, petit
/// rebond élastique au bout), ou glisser SUR LA ZONE DES POINTS seulement (ailleurs, l'appui sert à déplacer l'île).
/// <para>Performance : les tuiles ne sont relues (au plus 1 fois par seconde) que si <see cref="IsActive"/> (île ouverte sur la
/// grille), et seulement celles de la page visible. Les vues sont gardées d'une construction à l'autre (même id, même taille).</para>
/// </summary>
public sealed class TileGridView : Grid
{
    private readonly Grid _pagesHost = new() { ClipToBounds = false };
    private readonly TextBlock _hoverName = new();
    private readonly StackPanel _dots = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _dotsZone = new();
    private readonly StackPanel _empty = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Visibility = Visibility.Collapsed };
    private readonly List<Grid> _pages = [];
    private readonly Dictionary<(string Id, TileSize Size), TileFace> _faces = [];
    private readonly DispatcherTimer _refresh = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DispatcherTimer _nameOut = new() { Interval = TimeSpan.FromMilliseconds(140) };
    private string _signature = "";
    private int _page;
    private bool _active;
    private DateTime _lastWheel = DateTime.MinValue;
    private Point? _swipeStart;
    private TileFace? _hovered;

    /// <summary>Molette : au plus une page par ce délai (un pavé tactile envoie des dizaines de petits crans).</summary>
    private static readonly TimeSpan WheelCooldown = TimeSpan.FromMilliseconds(260);

    /// <summary>Clic sur une tuile.</summary>
    public event Action<TileFace>? TileClicked;

    /// <summary>Clic droit sur une tuile qui a une action de clic droit.</summary>
    public event Action<TileFace>? TileRightClicked;

    /// <summary>Page changée (l'île garde la même taille : rien à refaire de son côté).</summary>
    public event Action? PageChanged;

    public TileGridView()
    {
        Width = TileGridShape.PageWidth;
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileGridShape.PageHeight) });
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileGridShape.Footer) });
        Children.Add(_pagesHost);

        // Liste vide (toutes les tuiles retirées) : une explication plutôt qu'une île vide.
        var emptyIcon = new TextBlock { Text = Icons.Widgets, FontFamily = FontStack.Icons, FontSize = IconSize.XXL, HorizontalAlignment = HorizontalAlignment.Center };
        emptyIcon.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextTertiary));
        var emptyTitle = new TextBlock { Text = Loc.T("tiles.empty.title"), FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S4, 0, 0) };
        var emptyHint = new TextBlock { Text = Loc.T("tiles.empty.hint"), FontSize = TypeRamp.Footnote, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S1, 0, 0) };
        emptyHint.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
        _empty.Children.Add(emptyIcon);
        _empty.Children.Add(emptyTitle);
        _empty.Children.Add(emptyHint);
        Children.Add(_empty);

        // Rangée du bas : nom de la tuile survolée au centre, points des pages à droite (zone où l'on peut glisser).
        _hoverName.FontSize = TypeRamp.Caption;
        _hoverName.FontWeight = FontWeights.SemiBold;
        _hoverName.HorizontalAlignment = HorizontalAlignment.Center;
        _hoverName.VerticalAlignment = VerticalAlignment.Center;
        _hoverName.Opacity = 0;
        _hoverName.IsHitTestVisible = false;
        _hoverName.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
        SetRow(_hoverName, 1);
        Children.Add(_hoverName);

        _dotsZone.Width = TileGridShape.SwipeZone;
        _dotsZone.HorizontalAlignment = HorizontalAlignment.Right;
        _dotsZone.Background = Paint.HitTarget; // capte la souris sans se voir (fenêtre « layered »)
        _dotsZone.Padding = new Thickness(0, 0, TileGridShape.Gap / 2, 0);
        _dotsZone.Child = _dots;
        _dotsZone.MouseLeftButtonDown += DotsZone_MouseLeftButtonDown;
        _dotsZone.MouseMove += DotsZone_MouseMove;
        _dotsZone.MouseLeftButtonUp += DotsZone_MouseLeftButtonUp;
        _dotsZone.LostMouseCapture += (_, _) => EndSwipe();
        SetRow(_dotsZone, 1);
        Children.Add(_dotsZone);

        _refresh.Tick += (_, _) => RefreshVisible();
        _nameOut.Tick += (_, _) =>
        {
            _nameOut.Stop();
            if (_hovered == null) FadeName(0);
        };
        PreviewMouseWheel += OnWheel;
    }

    public int PageCount => _pages.Count;
    public int CurrentPage => _page;
    public bool IsEmpty => _pages.Count == 0;

    /// <summary>Page visible (pour la cascade à l'ouverture), ou la grille elle-même si elle est vide.</summary>
    public FrameworkElement CurrentPageElement => _pages.Count > 0 ? _pages[_page] : _empty;

    /// <summary>Toutes les tuiles construites (rendus, auto-test).</summary>
    public IEnumerable<TileFace> Faces => _pages.SelectMany(p => p.Children.OfType<TileFace>());

    /// <summary>Île ouverte sur la grille : les tuiles de la page visible sont relues chaque seconde ; sinon plus rien ne tourne.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            UpdateLive();
            if (value)
            {
                RefreshVisible();
                _refresh.Start();
            }
            else
            {
                _refresh.Stop();
                _hovered = null;
                _hoverName.BeginAnimation(OpacityProperty, null);
                _hoverName.Opacity = 0;
            }
        }
    }

    /// <summary>
    /// Tuiles vivantes : seules celles de la page affichée, île ouverte sur la grille, peuvent animer quelque chose (ciel,
    /// trotteuse, visualiseur) ; toutes les autres sont arrêtées.
    /// </summary>
    private void UpdateLive()
    {
        foreach (var face in _faces.Values) face.SetLive(_active && IsOnCurrentPage(face));
    }

    /// <summary>
    /// Pose les tuiles placées (<see cref="TileLayout.Place"/>) ; ne refait rien si la disposition n'a pas changé. Les vues déjà
    /// construites (même id, même taille) sont réutilisées ; la page affichée reste la même si elle existe encore.
    /// </summary>
    public void Show(IReadOnlyList<TileSlot> slots, Func<TileDefinition, TileSize, TileFace> create)
    {
        string signature = string.Join("|", slots.Select(s => $"{s.Id}:{s.Size}:{s.Page}:{s.Column}:{s.Row}"));
        if (signature == _signature) return;
        _signature = signature;
        foreach (var page in _pages)
        {
            Cascade.Clear(page);
            page.Children.Clear();
        }
        _pagesHost.Children.Clear();
        _pages.Clear();
        var keep = new HashSet<(string, TileSize)>();
        int count = slots.Count == 0 ? 0 : slots.Max(s => s.Page) + 1;
        for (int i = 0; i < count; i++)
        {
            var page = NewPage();
            _pages.Add(page);
            _pagesHost.Children.Add(page);
        }
        foreach (var slot in slots)
        {
            if (TileRegistry.Find(slot.Id) is not { } definition) continue;
            var key = (slot.Id, slot.Size);
            if (!_faces.TryGetValue(key, out var face))
            {
                face = create(definition, slot.Size);
                face.Clicked += f => TileClicked?.Invoke(f);
                face.RightClicked += f => TileRightClicked?.Invoke(f);
                face.MouseEnter += Face_MouseEnter;
                face.MouseLeave += Face_MouseLeave;
                _faces[key] = face;
            }
            keep.Add(key);
            var (columns, rows) = slot.Size.Span();
            face.Width = columns * TileGridShape.Cell + (columns - 1) * TileGridShape.Gap;
            face.Height = rows * TileGridShape.Cell + (rows - 1) * TileGridShape.Gap;
            face.Margin = new Thickness(TileGridShape.Gap / 2);
            face.HorizontalAlignment = HorizontalAlignment.Left;
            face.VerticalAlignment = VerticalAlignment.Top;
            SetColumn(face, slot.Column);
            SetRow(face, slot.Row);
            SetColumnSpan(face, columns);
            SetRowSpan(face, rows);
            _pages[slot.Page].Children.Add(face);
        }
        foreach (var gone in _faces.Keys.Where(k => !keep.Contains(k)).ToList())
        {
            _faces[gone].SetLive(false); // tuile retirée : ses animations s'arrêtent
            _faces.Remove(gone);
        }
        _empty.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _page = Math.Clamp(_page, 0, Math.Max(0, count - 1));
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].Opacity = i == _page ? 1 : 0;
            _pages[i].Visibility = i == _page ? Visibility.Visible : Visibility.Hidden;
            _pages[i].IsHitTestVisible = i == _page;
        }
        BuildDots();
        UpdateLive();
        if (_active) RefreshVisible();
    }

    private static Grid NewPage()
    {
        var page = new Grid { HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        for (int c = 0; c < TileGridShape.Columns; c++) page.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(TileGridShape.Cell + TileGridShape.Gap) });
        for (int r = 0; r < TileGridShape.Rows; r++) page.RowDefinitions.Add(new RowDefinition { Height = new GridLength(TileGridShape.Cell + TileGridShape.Gap) });
        Cascade.SetIsEnabled(page, true); // à l'ouverture, les tuiles arrivent une par une (O3)
        return page;
    }

    /// <summary>Vue d'une tuile posée (null si elle n'est pas dans la grille).</summary>
    public TileFace? FaceOf(string id) => Faces.FirstOrDefault(f => f.Definition.Id == id);

    /// <summary>Une tuile de la page affichée accepte des fichiers glissés (Téléphone) : l'île reste sur la grille pendant le glisser.</summary>
    public bool CurrentPageAcceptsFiles => _pages.Count > 0 && _pages[_page].Children.OfType<TileFace>().Any(f => f.AllowDrop);

    /// <summary>La tuile est sur la page affichée.</summary>
    public bool IsOnCurrentPage(TileFace face) => _pages.Count > 0 && ReferenceEquals(face.Parent, _pages[_page]);

    /// <summary>Relit toutes les tuiles construites (rendus --snapshot).</summary>
    public void RefreshAll()
    {
        foreach (var face in Faces) face.Refresh();
    }

    private void RefreshVisible()
    {
        if (_pages.Count == 0) return;
        foreach (var face in _pages[_page].Children.OfType<TileFace>()) face.Refresh();
    }

    // ------------------------------------------------------------------ pages

    /// <summary>Affiche la page <paramref name="index"/> (bornée) : elle glisse du côté où l'on va pendant que l'autre s'efface.</summary>
    public void ShowPage(int index, bool animate)
    {
        if (_pages.Count == 0) return;
        index = Math.Clamp(index, 0, _pages.Count - 1);
        if (index == _page) return;
        var from = _pages[_page];
        var to = _pages[index];
        int direction = Math.Sign(index - _page);
        _page = index;
        from.IsHitTestVisible = false;
        to.IsHitTestVisible = true;
        to.Visibility = Visibility.Visible;
        if (animate && MotionPrefs.Rich)
        {
            Fade(from, 0, Motion.Fast, hideAtEnd: true);
            Fade(to, 1, Motion.Medium, hideAtEnd: false);
            PageTransition.Play(from, to, direction, vertical: false, _pagesHost);
        }
        else
        {
            foreach (var page in new[] { from, to })
            {
                page.BeginAnimation(OpacityProperty, null);
                PageTransition.Stop(page);
            }
            from.Opacity = 0;
            from.Visibility = Visibility.Hidden;
            to.Opacity = 1;
        }
        BuildDots();
        _hovered = null;
        FadeName(0);
        UpdateLive();
        if (_active) RefreshVisible();
        PageChanged?.Invoke();
    }

    /// <summary>Page suivante (<paramref name="step"/> &gt; 0) ou précédente ; au bout : rebond élastique. Vrai si la page a changé.</summary>
    public bool Turn(int step, bool animate = true)
    {
        var (page, edge) = TileLayout.Turn(_page, _pages.Count, step);
        if (edge)
        {
            if (animate && _pages.Count > 0) Elastic.Kick(_pages[_page], step > 0 ? -1 : 1, vertical: false);
            return false;
        }
        bool changed = page != _page;
        ShowPage(page, animate);
        return changed;
    }

    private void OnWheel(object sender, MouseWheelEventArgs e)
    {
        if (_pages.Count == 0) return;
        // Une tuile qui veut la molette (Compteur) la reçoit : la page ne change pas sous elle.
        for (var node = e.OriginalSource as DependencyObject; node != null && node != this; node = VisualTreeHelper.GetParent(node))
            if (node is TileFace { WantsWheel: true }) return;
        e.Handled = true;
        var now = DateTime.UtcNow;
        if (now - _lastWheel < WheelCooldown) return;
        _lastWheel = now;
        Turn(e.Delta < 0 ? 1 : -1);
    }

    private static void Fade(UIElement element, double to, TimeSpan duration, bool hideAtEnd)
    {
        var animation = new DoubleAnimation(to, duration) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } };
        if (hideAtEnd)
            animation.Completed += (_, _) =>
            {
                if (!element.IsHitTestVisible) element.Visibility = Visibility.Hidden; // plus rien à dessiner
            };
        element.BeginAnimation(OpacityProperty, animation);
    }

    /// <summary>Rendus --snapshot : fondus de pages terminés.</summary>
    public void FinishAnimations()
    {
        for (int i = 0; i < _pages.Count; i++)
        {
            _pages[i].BeginAnimation(OpacityProperty, null);
            _pages[i].Opacity = i == _page ? 1 : 0;
            _pages[i].Visibility = i == _page ? Visibility.Visible : Visibility.Hidden;
        }
        foreach (var face in Faces) face.SettleMotion(); // tuiles vivantes : valeurs posées (rendus figés)
    }

    // ------------------------------------------------------------------ points des pages

    private void BuildDots()
    {
        _dots.Children.Clear();
        _dotsZone.Visibility = _pages.Count > 1 ? Visibility.Visible : Visibility.Collapsed;
        for (int i = 0; i < _pages.Count; i++)
        {
            int index = i;
            bool current = i == _page;
            var dot = new Rectangle
            {
                Width = current ? TileGridShape.DotActive : TileGridShape.Dot,
                Height = TileGridShape.Dot,
                RadiusX = TileGridShape.Dot / 2,
                RadiusY = TileGridShape.Dot / 2,
                VerticalAlignment = VerticalAlignment.Center,
            };
            dot.SetResourceReference(Shape.FillProperty, Paint.Key(current ? InkRole.TextPrimary : InkRole.TextTertiary));
            // Zone cliquable plus grande que le point.
            var hit = new Border
            {
                Background = Paint.HitTarget,
                MinWidth = TileGridShape.DotHit,
                Height = TileGridShape.Footer,
                Padding = new Thickness(TileGridShape.DotGap / 2, 0, TileGridShape.DotGap / 2, 0),
                Child = dot,
                Cursor = Cursors.Hand,
                ToolTip = Loc.F("tiles.page", i + 1, _pages.Count),
                Tag = index,
            };
            _dots.Children.Add(hit);
        }
    }

    private void DotsZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        e.Handled = true; // ici l'appui change de page : il ne déplace pas l'île
        _swipeStart = e.GetPosition(this);
        _dotsZone.CaptureMouse();
    }

    private void DotsZone_MouseMove(object sender, MouseEventArgs e)
    {
        if (_swipeStart is not { } start || _pages.Count == 0) return;
        // Pendant le glisser, la page suit un peu le doigt (élastique), sans jamais partir de plus de 14 DIP.
        double dx = e.GetPosition(this).X - start.X;
        if (MotionTransform.For(_pages[_page]) is { } parts) parts.Slide.X = Elastic.Rubber(dx, Kinetics.ElasticMax);
    }

    private void DotsZone_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (_swipeStart is not { } start) return;
        e.Handled = true;
        double dx = e.GetPosition(this).X - start.X;
        var source = e.OriginalSource as DependencyObject;
        EndSwipe();
        if (Math.Abs(dx) >= TileGridShape.SwipeThreshold)
        {
            Turn(dx < 0 ? 1 : -1); // vers la gauche = page suivante, comme sur un téléphone
            return;
        }
        // Simple clic : le point visé.
        for (var node = source; node != null && node != _dotsZone; node = VisualTreeHelper.GetParent(node))
            if (node is Border { Tag: int index })
            {
                ShowPage(index, animate: true);
                return;
            }
    }

    private void EndSwipe()
    {
        if (_swipeStart == null) return;
        _swipeStart = null;
        if (_dotsZone.IsMouseCaptured) _dotsZone.ReleaseMouseCapture();
        if (_pages.Count > 0 && MotionTransform.For(_pages[_page]) is { } parts) parts.Slide.X = 0; // la page revient en place
    }

    // ------------------------------------------------------------------ nom de la tuile survolée

    private void Face_MouseEnter(object sender, MouseEventArgs e)
    {
        if (sender is not TileFace face) return;
        _hovered = face;
        _nameOut.Stop();
        _hoverName.Text = face.Definition.Name;
        FadeName(1);
    }

    private void Face_MouseLeave(object sender, MouseEventArgs e)
    {
        if (!ReferenceEquals(sender, _hovered)) return;
        _hovered = null;
        _nameOut.Start(); // d'une tuile à sa voisine, le nom ne clignote pas
    }

    private void FadeName(double to)
    {
        if (!MotionPrefs.Rich)
        {
            _hoverName.BeginAnimation(OpacityProperty, null);
            _hoverName.Opacity = to;
            return;
        }
        _hoverName.BeginAnimation(OpacityProperty, new DoubleAnimation(to, to > 0 ? Motion.Base : Motion.Quick));
    }

    /// <summary>Rendus --snapshot : nom d'une tuile affiché comme si la souris était dessus.</summary>
    public void ShowHoverName(TileFace? face)
    {
        _hoverName.BeginAnimation(OpacityProperty, null);
        _hoverName.Text = face?.Definition.Name ?? "";
        _hoverName.Opacity = face == null ? 0 : 1;
    }
}
