using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Palette;

/// <summary>
/// Palette de commandes (raccourci global, Ctrl+Espace par défaut) : une barre de recherche qui sort de
/// l'île. La carte part de la pilule (même place, même taille) puis s'agrandit avec le ressort de l'île ; à la
/// fermeture (Échap, clic ailleurs, action lancée) elle y rentre. Fenêtre activable : on y tape au clavier.
/// </summary>
public partial class PaletteWindow : Window
{
    private const double ContentWidth = Sizes.PaletteWidth;
    private static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static PaletteWindow? _instance;

    private readonly Spring _width = new(IslandShape.Compact.Width);
    private readonly Spring _height = new(IslandShape.Compact.Height);
    private readonly Spring _radius = new(Radius.IslandCompact.Round);
    private readonly List<(SquircleBorder Row, PaletteItem Item)> _rows = [];
    private readonly PaletteActions _actions;
    private IslandWindow? _island;
    private int _selected;
    private bool _animating;
    private bool _closing;
    /// <summary>Île en bas de son écran : la carte s'ouvre vers le haut.</summary>
    private bool _upward;
    private TimeSpan _lastFrame;
    private readonly LiquidGlass _glass;

    private PaletteWindow()
    {
        InitializeComponent();
        _glass = new LiquidGlass(Card, () => Card.CornerRadius.TopLeft);
        Closed += (_, _) => _glass.Detach();
        _actions = new PaletteActions(duration => _island?.StartTimer(duration), () => _island?.StartPomodoro(),
            question => _island?.AskClaude(question), id => _island?.Surprise(id));
        SourceInitialized += (_, _) =>
        {
            // Ni barre des tâches ni Alt+Tab, mais activable (on doit pouvoir taper).
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, (GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) => BeginClose(); // clic ailleurs
        PreviewKeyDown += OnPreviewKeyDown;
    }

    /// <summary>
    /// Raccourci pressé : ouvre la palette, ou la referme si elle est déjà ouverte. <paramref name="text"/> : texte déjà
    /// tapé (« ? » quand on vient du bouton « Poser une question » de l'onglet Claude).
    /// </summary>
    public static void Toggle(IslandWindow? island, string? text = null)
    {
        _instance ??= new PaletteWindow();
        if (_instance.IsVisible && !_instance._closing) _instance.BeginClose();
        else _instance.Open(island, text);
    }

    private void Open(IslandWindow? island, string? text = null)
    {
        _island = island;
        _closing = false;
        var anchor = Anchor();
        PlaceFrom(anchor);
        SnapTo(anchor.Bounds.Width, anchor.Bounds.Height, anchor.Radius);

        Card.BorderBrush = new SolidColorBrush(AppearanceService.RimColor());
        AppearanceService.ApplyGlow(CardShadow, Shadows.Window);
        QueryBox.Text = text ?? "";
        QueryBox.CaretIndex = QueryBox.Text.Length;
        UpdateResults();
        CardContent.BeginAnimation(OpacityProperty, null);
        CardContent.Opacity = 0;

        Show();
        Activate();
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);
        island?.SetPaletteOpen(true);

        Fade(CardContent, 1, Motion.Base, Motion.Delay);
        _width.SetTarget(ContentWidth + 2 * Stroke.Hairline, Springs.Open);
        _height.SetTarget(ContentHeight(), Springs.Open);
        _radius.SetTarget(AppearanceService.Radius(Radius.Palette), Springs.Open);
        StartAnimation();
        _ = RefreshListsAsync();
    }

    /// <summary>Applis et dossiers chargés en arrière-plan : les résultats se complètent dès qu'ils sont prêts.</summary>
    private async Task RefreshListsAsync()
    {
        try
        {
            await Task.WhenAll(AppCatalog.RefreshAsync(), FolderIndex.RefreshAsync());
            if (IsVisible && !_closing && QueryBox.Text.Trim().Length > 0) UpdateResults();
        }
        catch (Exception ex)
        {
            Log.Error("Palette : chargement des listes", ex);
        }
    }

    private void BeginClose()
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        Fade(CardContent, 0, Motion.Fast);
        var end = Anchor();
        _width.SetTarget(end.Bounds.Width, Springs.Close);
        _height.SetTarget(end.Bounds.Height, Springs.Close);
        _radius.SetTarget(end.Radius, Springs.Close);
        StartAnimation();
    }

    private void FinishClose()
    {
        _closing = false;
        Hide();
        _island?.SetPaletteOpen(false); // la pilule réapparaît exactement là où la carte s'est refermée
    }

    /// <summary>La pilule de l'île (unités WPF de l'écran) ; sans île affichée, une pilule en haut de l'écran principal.</summary>
    private IslandAnchor Anchor()
    {
        if (_island?.PaletteAnchor() is { } anchor) return anchor;
        var (bounds, _, scale) = ScreenInfo.Primary();
        var screen = new Rect(bounds.Left / scale, bounds.Top / scale, (bounds.Right - bounds.Left) / scale, (bounds.Bottom - bounds.Top) / scale);
        var pill = IslandShape.Compact;
        return new IslandAnchor(new Rect(screen.Left + screen.Width / 2 - pill.Width / 2, screen.Top + IslandShape.TopMargin, pill.Width, pill.Height),
            AppearanceService.Radius(Radius.IslandCompact), false, screen);
    }

    /// <summary>
    /// Fenêtre posée pour que la carte parte exactement de la pilule et grandisse vers l'intérieur de l'écran : vers le bas
    /// (île en haut) ou vers le haut (île en bas), centrée sur la pilule si elle tient, sinon depuis le bord gauche / droit.
    /// </summary>
    private void PlaceFrom(IslandAnchor anchor)
    {
        var pill = anchor.Bounds;
        double half = (ContentWidth + 2 * Stroke.Hairline) / 2 + Shadows.WindowMargin;
        double center = pill.Left + pill.Width / 2;
        var horizontal = center - half < anchor.Screen.Left ? HorizontalAlignment.Left
            : center + half > anchor.Screen.Right ? HorizontalAlignment.Right
            : HorizontalAlignment.Center;
        double side = horizontal == HorizontalAlignment.Center ? 0 : IslandShape.EdgeMargin;
        _upward = anchor.Upward;
        Scroller.Height = _upward ? Scroller.MaxHeight : double.NaN; // pied de la carte toujours en bas

        CardHost.HorizontalAlignment = Card.HorizontalAlignment = horizontal;
        CardHost.VerticalAlignment = Card.VerticalAlignment = _upward ? VerticalAlignment.Bottom : VerticalAlignment.Top;
        Card.Margin = new Thickness(horizontal == HorizontalAlignment.Left ? side : 0, _upward ? 0 : IslandShape.TopMargin,
            horizontal == HorizontalAlignment.Right ? side : 0, _upward ? IslandShape.TopMargin : 0);
        Left = horizontal switch
        {
            HorizontalAlignment.Left => pill.Left - side,
            HorizontalAlignment.Right => pill.Right + side - Width,
            _ => center - Width / 2,
        };
        Top = _upward ? pill.Bottom + IslandShape.TopMargin - Height : pill.Top - IslandShape.TopMargin;
    }

    // ------------------------------------------------------------------ résultats

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e) => UpdateResults();

    private void UpdateResults()
    {
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        _rows.Clear();
        Results.Children.Clear();
        try
        {
            foreach (var item in PaletteEngine.Search(QueryBox.Text, _actions)) AddRow(item);
        }
        catch (Exception ex)
        {
            Log.Error("Palette : recherche", ex);
        }
        Select(0);
        if (IsVisible && !_closing)
        {
            _height.SetTarget(ContentHeight(), Springs.Reflow);
            StartAnimation();
        }
    }

    private void AddRow(PaletteItem item)
    {
        int index = _rows.Count;
        UIElement icon = item.Icon != null
            ? new Image { Source = item.Icon, Width = Sizes.GlyphTile, Height = Sizes.GlyphTile }
            : new SquircleBorder
            {
                Width = Sizes.GlyphTile,
                Height = Sizes.GlyphTile,
                CornerRadius = Corners.S,
                Background = item.GlyphBackground ?? Paint.SurfaceHover,
                Child = new TextBlock
                {
                    Text = item.Glyph,
                    FontFamily = FontStack.Icons,
                    FontSize = IconSize.L,
                    Foreground = Paint.White,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        RenderOptions.SetBitmapScalingMode(icon, BitmapScalingMode.HighQuality);

        var texts = new StackPanel { Margin = new Thickness(Space.S6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = item.Title, FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock
        {
            Text = item.Subtitle,
            FontSize = TypeRamp.Caption,
            Foreground = Paint.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
        });

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(texts, 1);
        content.Children.Add(icon);
        content.Children.Add(texts);

        var row = new SquircleBorder
        {
            CornerRadius = Corners.M,
            Padding = new Thickness(Space.S5, Space.S4, Space.S5, Space.S4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
            Child = content,
        };
        row.MouseEnter += (_, _) => Select(index);
        row.MouseLeftButtonUp += (_, _) => Execute(item);
        Results.Children.Add(row);
        _rows.Add((row, item));
    }

    private void Select(int index)
    {
        if (_rows.Count == 0) return;
        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        for (int i = 0; i < _rows.Count; i++) _rows[i].Row.Background = i == _selected ? Paint.Selected : Brushes.Transparent;
        _rows[_selected].Row.BringIntoView();
    }

    private void Execute(PaletteItem item)
    {
        if (item.FillQuery is { } text)
        {
            QueryBox.Text = text;
            QueryBox.CaretIndex = text.Length;
            QueryBox.Focus();
            return;
        }
        try
        {
            item.Run();
        }
        catch (Exception ex)
        {
            Log.Error($"Palette : « {item.Title} »", ex);
        }
        BeginClose();
    }

    private static readonly Key[] SecretCode = [Key.Up, Key.Up, Key.Down, Key.Down, Key.Left, Key.Right, Key.Left, Key.Right, Key.B, Key.A];
    private readonly List<Key> _typed = [];

    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        _typed.Add(e.Key);
        if (_typed.Count > SecretCode.Length) _typed.RemoveAt(0);
        if (_typed.SequenceEqual(SecretCode))
        {
            _typed.Clear();
            BeginClose();
            _island?.Surprise("konami");
            e.Handled = true;
            return;
        }
        switch (e.Key)
        {
            case Key.Escape:
                BeginClose();
                break;
            case Key.Down:
                Select(_selected + 1);
                break;
            case Key.Up:
                Select(_selected - 1);
                break;
            case Key.Enter:
                if (_rows.Count > 0) Execute(_rows[_selected].Item);
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ animation ressort

    /// <summary>
    /// Hauteur de la carte ouverte. Calculée ligne par ligne : mesurer la carte entière rendrait l'ancienne
    /// hauteur (WPF ne propage l'invalidation des enfants qu'au prochain passage de mise en page).
    /// </summary>
    private double ContentHeight()
    {
        var unlimited = new Size(ContentWidth, double.PositiveInfinity);
        double rows = 0;
        foreach (var (row, _) in _rows)
        {
            row.Measure(new Size(ContentWidth - Scroller.Margin.Left - Scroller.Margin.Right, double.PositiveInfinity));
            rows += row.DesiredSize.Height;
        }
        Header.Measure(unlimited);
        Footer.Measure(unlimited);
        // Ouverte vers le haut : hauteur fixe, sinon la barre de recherche bougerait sous les doigts à chaque lettre.
        double list = _upward ? Scroller.MaxHeight : Math.Min(rows, Scroller.MaxHeight);
        return Header.DesiredSize.Height + Stroke.Hairline + Scroller.Margin.Top + list
               + Footer.DesiredSize.Height + 2 * Stroke.Hairline;
    }

    private void SnapTo(double width, double height, double radius)
    {
        foreach (var (spring, value) in new[] { (_width, width), (_height, height), (_radius, radius) })
        {
            spring.SetTarget(value, Springs.Snap);
            spring.Snap();
        }
        ApplyGeometry();
    }

    private void StartAnimation()
    {
        if (_animating) return;
        _animating = true;
        _lastFrame = TimeSpan.Zero;
        CompositionTarget.Rendering += OnRendering;
    }

    private void OnRendering(object? sender, EventArgs e)
    {
        var now = ((RenderingEventArgs)e).RenderingTime;
        if (now == _lastFrame) return;
        double dt = _lastFrame == TimeSpan.Zero ? 1 / 60.0 : Math.Min((now - _lastFrame).TotalSeconds, 1 / 30.0);
        _lastFrame = now;
        dt *= AppearanceService.Speed;

        bool settled = _width.Step(dt) & _height.Step(dt) & _radius.Step(dt);
        ApplyGeometry();
        if (!settled) return;
        CompositionTarget.Rendering -= OnRendering;
        _animating = false;
        if (_closing) FinishClose();
    }

    private void ApplyGeometry()
    {
        double width = Math.Max(_width.Value, 20), height = Math.Max(_height.Value, 6);
        double radius = Math.Clamp(_radius.Value, 0, Math.Min(width, height) / 2);
        Card.Width = width;
        Card.Height = height;
        Card.CornerRadius = new CornerRadius(radius);
        _glass.Refresh();
        // Contenu à sa largeur finale, centré et découpé à la forme de la carte pendant l'animation.
        CardContent.Margin = new Thickness((width - 2 * Stroke.Hairline - ContentWidth) / 2, 0, 0, 0);
    }

    private static void Fade(UIElement element, double to, TimeSpan duration, TimeSpan? delay = null) =>
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(to, duration)
        {
            BeginTime = delay ?? TimeSpan.Zero,
            EasingFunction = EaseOut,
            SpeedRatio = AppearanceService.Speed,
        });

    /// <summary>Mode --snapshot : palette ouverte (vide, calcul, minuteur, recherche), hors écran.</summary>
    internal static async Task RenderSnapshotsAsync(IslandWindow island, string folder)
    {
        await Task.WhenAll(AppCatalog.RefreshAsync(), FolderIndex.RefreshAsync());
        var window = new PaletteWindow { ShowActivated = false, Left = -30000, Top = 0, _island = island };
        window.Card.BorderBrush = new SolidColorBrush(AppearanceService.RimColor());
        AppearanceService.ApplyGlow(window.CardShadow, Shadows.Window);
        window.Show();
        foreach (var (query, name) in new[]
                 {
                     ("", "8-palette"), ("15% de 80", "8b-palette-calcul"), ("timer 10 min", "8c-palette-minuteur"), ("ga", "8d-palette-recherche"),
                 })
        {
            window.QueryBox.Text = query;
            window.UpdateResults();
            window.CardContent.Opacity = 1;
            window.SnapTo(ContentWidth + 2 * Stroke.Hairline, window.ContentHeight(), AppearanceService.Radius(Radius.Palette));
            await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, name + ".png"));
        }
        window.Close();
    }
}
