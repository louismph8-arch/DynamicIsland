using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Horloges » de l'île (6C) : une carte par ville (4 au plus) avec l'heure, le jour (hier / aujourd'hui / demain),
/// le décalage avec l'heure du PC et un soleil ou une lune. Calculé sur place (fuseaux de Windows), mis à jour toutes les
/// 15 s seulement quand l'onglet est ouvert. Les villes s'ajoutent dans Paramètres → Île → « Horloges du monde ».
/// </summary>
public sealed class WorldClockView : UserControl
{
    private readonly StackPanel _cards = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center };
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(15) };
    private bool _active;

    public WorldClockView()
    {
        Width = IslandShape.ContentWidth;
        Content = _cards;
        _timer.Tick += (_, _) => Refresh();
    }

    /// <summary>Onglet ouvert : on met l'heure à jour régulièrement ; fermé : rien ne tourne.</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            if (value)
            {
                Refresh();
                _timer.Start();
            }
            else
            {
                _timer.Stop();
            }
        }
    }

    public void Refresh()
    {
        _cards.Children.Clear();
        foreach (var clock in SettingsStore.Current.Island.WorldClocks)
            if (WorldClockService.Now(clock) is { } now) _cards.Children.Add(Card(clock, now.Time, now.Offset));
    }

    private static SquircleBorder Card(WorldClock clock, DateTime time, double offset)
    {
        bool day = time.Hour is >= 7 and < 19;
        int days = (time.Date - DateTime.Now.Date).Days;
        string gap = offset == 0 ? Loc.T("clock.same") : Loc.F("clock.offset", (offset > 0 ? "+" : "−") + Math.Abs(offset).ToString("0.#", Loc.Culture));
        // Le jour seulement s'il change (« Demain · +13 h ») : « Aujourd'hui · +5 h » ne tient pas dans une carte.
        string line = days == 0 ? gap : $"{Loc.T(days < 0 ? "clock.yesterday" : "clock.tomorrow")} · {gap}";

        var panel = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S2, 0, Space.S2, 0) };
        panel.Children.Add(new TextBlock
        {
            Text = day ? Icons.WeatherSun : Icons.WeatherMoon,
            FontFamily = FontStack.Icons,
            FontSize = IconSize.L,
            Foreground = day ? Paint.Timer : Paint.TextSecondary,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text = time.ToString("t", Loc.Culture),
            FontFamily = FontStack.Display,
            FontSize = TypeRamp.Title2,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
            Margin = new Thickness(0, Space.S2, 0, Space.S1),
        });
        panel.Children.Add(new TextBlock
        {
            Text = clock.Name.Split(',')[0], // « Paris », sans le pays
            FontSize = TypeRamp.Footnote,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
            ToolTip = clock.Name,
        });
        panel.Children.Add(new TextBlock
        {
            Text = line,
            FontSize = TypeRamp.Caption2,
            Foreground = Paint.TextSecondary,
            TextTrimming = TextTrimming.CharacterEllipsis,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        return new SquircleBorder
        {
            Width = IslandShape.ClockCardWidth,
            Height = IslandShape.ClockCardHeight,
            Margin = new Thickness(Space.S1, 0, Space.S1, 0),
            CornerRadius = Corners.L,
            Background = Paint.Control,
            Child = panel,
        };
    }
}
