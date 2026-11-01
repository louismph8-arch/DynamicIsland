using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile Compteur : un nombre en grand. Clic = +1, molette = ±1 (la page ne change pas sous la tuile), clic droit = −1 / remettre
/// à 0 ; en W et L, boutons − et + (et ↺ en L). Valeur enregistrée dans les réglages (<see cref="EverydayTileSettings.Counter"/>),
/// sauvegarde différée comme tout réglage.
/// </summary>
public sealed class CounterFace : EverydayFace
{
    private readonly IconChip _chip = TilePieces.Chip(Icons.Counter);
    private readonly TextBlock _number;
    private readonly ScaleTransform _bump = new(1, 1);
    private long _shown = long.MinValue;

    public CounterFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        _number = TilePieces.Number(BaseSize(size));
        _number.RenderTransformOrigin = new Point(0.5, 0.5);
        _number.RenderTransform = _bump;
        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto);
                Root.Children.Add(_chip);
                _number.VerticalAlignment = VerticalAlignment.Bottom;
                Root.Children.Add(Place(_number, 1));
                Root.Children.Add(Place(TilePieces.Name(definition.Name), 2));
                break;
            case TileSize.W:
            {
                Rows(Auto, Star);
                Root.Children.Add(Header(definition));
                var row = new Grid { VerticalAlignment = VerticalAlignment.Bottom };
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                _number.VerticalAlignment = VerticalAlignment.Bottom;
                row.Children.Add(_number);
                var minus = TilePieces.GlyphButton(Icons.Minus, Loc.T("tiles.counter.minus"), () => Add(-1), Sizes.SmallIconButton);
                var plus = TilePieces.GlyphButton(Icons.Plus, Loc.T("tiles.counter.plus"), () => Add(1), Sizes.SmallIconButton);
                minus.VerticalAlignment = plus.VerticalAlignment = VerticalAlignment.Bottom;
                plus.Margin = new Thickness(EverydayTileShape.PresetGap, 0, 0, 0);
                row.Children.Add(Place(minus, 0, 1));
                row.Children.Add(Place(plus, 0, 2));
                Root.Children.Add(Place(row, 1));
                break;
            }
            default:
            {
                Rows(Auto, Star, Auto, Auto);
                Root.Children.Add(Header(definition));
                _number.HorizontalAlignment = HorizontalAlignment.Center;
                _number.VerticalAlignment = VerticalAlignment.Center;
                Root.Children.Add(Place(_number, 1));
                var buttons = new Grid { Margin = new Thickness(0, 0, 0, Space.S3) };
                for (int i = 0; i < 3; i++) buttons.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                var minus = TilePieces.GlyphButton(Icons.Minus, Loc.T("tiles.counter.minus"), () => Add(-1), Sizes.SoftButton);
                var reset = TilePieces.GlyphButton(Icons.Reset, Loc.T("tiles.counter.reset"), Reset, Sizes.SoftButton);
                var plus = TilePieces.GlyphButton(Icons.Plus, Loc.T("tiles.counter.plus"), () => Add(1), Sizes.SoftButton);
                foreach (var (button, column) in new[] { (minus, 0), (reset, 1), (plus, 2) })
                {
                    button.Width = double.NaN;
                    button.HorizontalAlignment = HorizontalAlignment.Stretch;
                    button.Margin = new Thickness(column == 0 ? 0 : EverydayTileShape.PresetGap / 2, 0, column == 2 ? 0 : EverydayTileShape.PresetGap / 2, 0);
                    buttons.Children.Add(Place(button, 0, column));
                }
                Root.Children.Add(Place(buttons, 2));
                var hint = TilePieces.Info();
                hint.Text = Loc.T("tiles.counter.hint");
                hint.HorizontalAlignment = HorizontalAlignment.Center;
                Root.Children.Add(Place(hint, 3));
                break;
            }
        }
    }

    private FrameworkElement Header(TileDefinition definition)
    {
        var header = new Grid();
        header.Children.Add(_chip);
        var name = TilePieces.Name(definition.Name);
        name.VerticalAlignment = VerticalAlignment.Center;
        name.Margin = new Thickness(Sizes.IconChip + Space.S3, 0, 0, 0);
        header.Children.Add(name);
        return header;
    }

    private static double BaseSize(TileSize size) => size switch
    {
        TileSize.L => EverydayTileShape.CounterLarge,
        TileSize.S => TypeRamp.LargeTitle, // la case S garde la place du nom
        _ => TypeRamp.Display,
    };

    /// <summary>Taille du chiffre : plus petite quand il s'allonge (il tient toujours dans sa case ; calcul pur).</summary>
    internal static double FitSize(TileSize size, string text)
    {
        double size0 = BaseSize(size);
        int length = text.Length;
        int roomy = size == TileSize.S ? 3 : size == TileSize.W ? 5 : 4;
        if (length <= roomy) return size0;
        return size == TileSize.S
            ? length <= 5 ? TypeRamp.Title1 : TypeRamp.NumberTile
            : length <= roomy + 2 ? TypeRamp.Display : TypeRamp.Title1;
    }

    internal static long Value => SettingsStore.Current.TileData.Counter;

    /// <summary>Change la valeur (bornée), l'enregistre (sauvegarde différée) et la montre tout de suite.</summary>
    internal void Add(long delta)
    {
        var data = SettingsStore.Current.TileData;
        long next = Math.Clamp(data.Counter + delta, -EverydayTileSettings.MaxCounter, EverydayTileSettings.MaxCounter);
        if (next == data.Counter) return;
        data.Counter = next;
        SettingsStore.ScheduleSave(); // pas de Update : un clic ne doit pas réveiller tous les modules
        Refresh();
        Bump();
    }

    private void Reset()
    {
        if (Value == 0) return;
        SettingsStore.Current.TileData.Counter = 0;
        SettingsStore.ScheduleSave();
        Refresh();
    }

    /// <summary>Petit sursaut du chiffre (seulement si les animations sont permises).</summary>
    private void Bump()
    {
        if (!MotionPrefs.Rich) return;
        var animation = new DoubleAnimation(EverydayTileShape.CounterBump, 1, Motion.Short) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut } };
        _bump.BeginAnimation(ScaleTransform.ScaleXProperty, animation);
        _bump.BeginAnimation(ScaleTransform.ScaleYProperty, animation);
    }

    public override bool WantsWheel => true;

    protected override bool OnTileClick()
    {
        Add(1);
        return true; // le Compteur n'a pas d'outil : le clic compte
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        e.Handled = true;
        Add(e.Delta > 0 ? 1 : -1);
    }

    protected override void OnMouseRightButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonUp(e);
        e.Handled = true; // le menu du Compteur, pas celui de l'île
        var menu = new ContextMenu { PlacementTarget = this, Placement = PlacementMode.Bottom };
        var minus = new MenuItem { Header = Loc.T("tiles.counter.minus") };
        minus.Click += (_, _) => Add(-1);
        var reset = new MenuItem { Header = Loc.T("tiles.counter.reset"), IsEnabled = Value != 0 };
        reset.Click += (_, _) => Reset();
        menu.Items.Add(minus);
        menu.Items.Add(reset);
        menu.IsOpen = true;
    }

    protected override void Update()
    {
        long value = Value;
        if (value == _shown) return;
        _shown = value;
        string text = value.ToString("N0", Loc.Culture);
        _number.Text = text;
        Numerals.Apply(_number, FitSize(TileSize, text));
        _number.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(InkRole.TextPrimary));
        _chip.IsActive = value != 0;
    }
}
