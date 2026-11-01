using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Design;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile Café : garde le PC éveillé (<see cref="KeepAwake"/>). Clic = marche / arrêt (avec la dernière durée choisie) ; en W et
/// L, boutons ∞ / 30 min / 1 h / 2 h ; clic droit = les mêmes durées + Arrêter. Quand il est actif : tasse en dégradé d'accent
/// sur sa pastille (comme une icône active) et petite fumée qui monte — animée seulement tuile en vue, île ouverte, sans
/// « Réduire les animations » ni mode Gaming, ≤ 30 images/s ; sinon la fumée reste figée.
/// </summary>
public sealed class CoffeeFace : EverydayFace
{
    private readonly SquircleBorder _halo;
    private readonly TextBlock _cup;
    private readonly Canvas _steam = new() { IsHitTestVisible = false, ClipToBounds = false };
    private readonly Path[] _wisps = new Path[3];
    private readonly TextBlock _state = TilePieces.Info();
    private readonly List<(TimeSpan? Choice, SquircleBorder Button)> _choices = [];
    private readonly DispatcherTimer _frame = new() { Interval = TimeSpan.FromSeconds(1.0 / Motion.DecorativeFrameRate) };
    private readonly double _cupSize, _haloSize;
    private string _shown = "";

    public CoffeeFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        _haloSize = size switch { TileSize.S => Sizes.IconChipLarge + Space.S3, TileSize.W => EverydayTileShape.CupLarge, _ => EverydayTileShape.CupLarge + Space.S8 };
        _cupSize = size switch { TileSize.S => IconSize.XXL, TileSize.W => IconSize.Huge, _ => EverydayTileShape.CupLarge };
        _halo = IconChip.CreateHalo(Corners.XL);
        _halo.Width = _halo.Height = _haloSize;
        _cup = TilePieces.Glyph(Icons.Coffee, _cupSize, InkRole.TextSecondary);
        var cup = new Grid { Width = _haloSize, Height = _haloSize, ClipToBounds = false };
        cup.Children.Add(_halo);
        cup.Children.Add(_cup);
        BuildSteam(cup);
        _frame.Tick += (_, _) => DrawSteam(DateTime.Now);

        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto, Auto);
                cup.HorizontalAlignment = HorizontalAlignment.Left;
                Root.Children.Add(cup);
                Root.Children.Add(Place(TilePieces.Name(definition.Name), 2));
                Root.Children.Add(Place(_state, 3));
                break;
            case TileSize.W:
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                cup.VerticalAlignment = VerticalAlignment.Center;
                Root.Children.Add(cup);
                var right = new Grid { Margin = new Thickness(Space.S5, 0, 0, 0) };
                foreach (var height in new[] { Auto, Auto, Star, Auto }) right.RowDefinitions.Add(new RowDefinition { Height = height });
                right.Children.Add(TilePieces.Name(definition.Name));
                right.Children.Add(Place(_state, 1));
                right.Children.Add(Place(ChoiceRow(stretch: false), 3));
                Root.Children.Add(Place(right, 0, 1));
                break;
            }
            default:
            {
                Rows(Star, Auto, Auto, Auto);
                cup.HorizontalAlignment = HorizontalAlignment.Center;
                cup.VerticalAlignment = VerticalAlignment.Center;
                cup.Margin = new Thickness(0, EverydayTileShape.SteamHeight, 0, 0);
                Root.Children.Add(cup);
                _state.HorizontalAlignment = HorizontalAlignment.Center;
                _state.Margin = new Thickness(0, Space.S2, 0, Space.S4);
                Root.Children.Add(Place(_state, 1));
                Root.Children.Add(Place(ChoiceRow(stretch: true), 2));
                var name = TilePieces.Name(definition.Name);
                name.Margin = new Thickness(0, Space.S3, 0, 0);
                Root.Children.Add(Place(name, 3));
                break;
            }
        }
    }

    /// <summary>Trois volutes de fumée au-dessus de la tasse (posées sur le haut de la pastille, elles en dépassent un peu).</summary>
    private void BuildSteam(Grid cup)
    {
        double width = _cupSize * 0.5;
        _steam.Width = width;
        _steam.Height = EverydayTileShape.SteamHeight;
        _steam.HorizontalAlignment = HorizontalAlignment.Center;
        _steam.VerticalAlignment = VerticalAlignment.Top;
        // La fumée part du haut de la tasse (le glyphe Lucide occupe ~85 % de sa taille, centrée dans la pastille).
        double cupTop = (_haloSize - _cupSize * 0.85) / 2;
        _steam.Margin = new Thickness(0, cupTop - EverydayTileShape.SteamHeight * 0.85, 0, 0);
        for (int i = 0; i < _wisps.Length; i++)
        {
            double x = width * (i + 0.5) / _wisps.Length;
            double h = EverydayTileShape.SteamHeight;
            var figure = new PathFigure { StartPoint = new Point(x, h) };
            figure.Segments.Add(new BezierSegment(new Point(x - h * 0.22, h * 0.66), new Point(x + h * 0.22, h * 0.33), new Point(x, 0), true));
            var geometry = new PathGeometry { Figures = { figure } };
            geometry.Freeze();
            var wisp = new Path
            {
                Data = geometry,
                StrokeThickness = EverydayTileShape.SteamStroke,
                StrokeStartLineCap = PenLineCap.Round,
                StrokeEndLineCap = PenLineCap.Round,
                RenderTransform = new TranslateTransform(),
                Opacity = 0,
            };
            wisp.SetResourceReference(Shape.StrokeProperty, Paint.Key(InkRole.TextSecondary));
            _wisps[i] = wisp;
            _steam.Children.Add(wisp);
        }
        cup.Children.Add(_steam);
    }

    private FrameworkElement ChoiceRow(bool stretch)
    {
        var row = new Grid { HorizontalAlignment = stretch ? HorizontalAlignment.Stretch : HorizontalAlignment.Left };
        for (int i = 0; i < KeepAwake.Choices.Length; i++)
        {
            var choice = KeepAwake.Choices[i];
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = stretch ? new GridLength(1, GridUnitType.Star) : GridLength.Auto });
            string tip = Loc.F("tiles.coffee.for", KeepAwake.ChoiceLabel(choice));
            var button = choice == null
                ? TilePieces.GlyphButton(Icons.Always, tip, () => Choose(null), EverydayTileShape.PresetHeight)
                : TilePieces.TextButton(ShortLabel(choice.Value), tip, () => Choose(choice));
            if (choice == null)
            {
                button.Width = double.NaN;
                button.MinWidth = EverydayTileShape.PresetMinWidth;
            }
            button.Margin = new Thickness(i == 0 ? 0 : EverydayTileShape.PresetGap, 0, 0, 0);
            Grid.SetColumn(button, i);
            row.Children.Add(button);
            _choices.Add((choice, button));
        }
        return row;
    }

    /// <summary>« 30 », « 1 h », « 2 h » (le libellé complet est dans l'info-bulle).</summary>
    private static string ShortLabel(TimeSpan choice) =>
        choice.TotalMinutes < 60 ? choice.TotalMinutes.ToString("0", Loc.Culture) : Loc.F("tiles.coffee.choice_h", choice.TotalHours);

    private void Choose(TimeSpan? choice)
    {
        KeepAwake.Start(choice);
        Refresh();
    }

    protected override bool OnTileClick()
    {
        KeepAwake.Toggle();
        Refresh();
        return true; // le Café n'a pas d'outil : le clic sert d'interrupteur
    }

    protected override void OnMouseRightButtonUp(System.Windows.Input.MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true; // le menu du Café, pas celui de l'île
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom };
        menu.Items.Add(new MenuItem { Header = StateText(), IsEnabled = false, FontWeight = FontWeights.SemiBold });
        menu.Items.Add(new Separator());
        foreach (var choice in KeepAwake.Choices)
        {
            var item = new MenuItem
            {
                Header = Loc.F("tiles.coffee.for", KeepAwake.ChoiceLabel(choice)),
                IsCheckable = true,
                IsChecked = KeepAwake.IsOn && KeepAwake.LastChoice == choice,
            };
            item.Click += (_, _) => Choose(choice);
            menu.Items.Add(item);
        }
        if (KeepAwake.IsOn)
        {
            var stop = new MenuItem { Header = Loc.T("tiles.coffee.stop") };
            stop.Click += (_, _) =>
            {
                KeepAwake.Stop();
                Refresh();
            };
            menu.Items.Add(stop);
        }
        menu.IsOpen = true;
    }

    /// <summary>« Actif · encore 1 h 25 » / « Actif jusqu'à l'arrêt » / « Veille normale ».</summary>
    private static string StateText() =>
        !KeepAwake.IsOn ? Loc.T("tiles.coffee.off")
        : KeepAwake.Remaining is { } left ? Loc.F("tiles.coffee.left", KeepAwake.FormatRemaining(left))
        : Loc.T("tiles.coffee.on_always");

    protected override void Update()
    {
        bool on = KeepAwake.IsOn;
        // Case S : texte court (« encore 1 h 00 », « Toujours ») pour tenir sur une ligne.
        string state = !Small || !on ? StateText()
            : KeepAwake.Remaining is { } left ? Loc.F("tiles.coffee.short_left", KeepAwake.FormatRemaining(left)) : Loc.T("tiles.coffee.short_always");
        string key = $"{on}|{state}|{KeepAwake.LastChoice}";
        if (key != _shown)
        {
            _shown = key;
            _state.Text = state;
            IconChip.Paint(_cup, _halo, on, Paint.TextSecondary);
            if (!on) _cup.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextSecondary));
            foreach (var (choice, button) in _choices) TilePieces.SetSelected(button, on && KeepAwake.LastChoice == choice);
        }
        SyncSteam();
    }

    protected override void OnLiveChanged(bool live) => SyncSteam();

    /// <summary>La fumée bouge seulement si tout le permet ; sinon elle est figée (Café actif) ou cachée (arrêté).</summary>
    internal bool SteamAnimating => _frame.IsEnabled;

    /// <summary>--self-test : la fumée peut s'animer en mode développeur (les rendus --snapshot, eux, la gardent figée).</summary>
    internal static bool AnimateInTests { get; set; }

    private void SyncSteam()
    {
        bool on = KeepAwake.IsOn;
        bool animate = on && IsLive && MotionPrefs.Rich && OpalTheme.EffectsOn && (!App.DeveloperMode || AnimateInTests);
        if (animate)
        {
            if (!_frame.IsEnabled) _frame.Start();
            return;
        }
        _frame.Stop();
        if (on) DrawSteam(null);
        else foreach (var wisp in _wisps) wisp.Opacity = 0;
    }

    /// <summary>Volutes à un instant (null = pose figée) : chacune monte en s'effaçant, décalée d'un tiers de cycle.</summary>
    private void DrawSteam(DateTime? now)
    {
        double cycle = EverydayTileShape.SteamCycle.TotalSeconds;
        double t = now is { } time ? time.TimeOfDay.TotalSeconds % cycle / cycle : 0.5;
        for (int i = 0; i < _wisps.Length; i++)
        {
            double phase = (t + i / (double)_wisps.Length) % 1;
            if (now == null) phase = 0.35 + 0.15 * i; // pose figée : trois volutes à des hauteurs différentes
            var (rise, opacity) = SteamPose(phase);
            ((TranslateTransform)_wisps[i].RenderTransform).Y = rise;
            _wisps[i].Opacity = opacity;
        }
    }

    /// <summary>Volute à la phase 0–1 du cycle : elle monte de <see cref="EverydayTileShape.SteamRise"/> et apparaît puis s'efface (calcul pur).</summary>
    internal static (double Rise, double Opacity) SteamPose(double phase)
    {
        phase = Math.Clamp(phase, 0, 1);
        return (-EverydayTileShape.SteamRise * phase, EverydayTileShape.SteamMaxOpacity * Math.Sin(Math.PI * phase));
    }
}
