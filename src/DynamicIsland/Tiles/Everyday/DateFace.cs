using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Tiles.Everyday;

/// <summary>
/// Tuile Date : jour de la semaine (accent), grand numéro du jour en chiffres fins, mois ; en W la semaine en plus, en L le
/// mini-mois (premier jour de la semaine selon la langue, aujourd'hui sur une pastille d'accent). Redessinée seulement quand la
/// date change (minuit). Clic = outil Calendrier.
/// </summary>
public sealed class DateFace : EverydayFace
{
    private readonly TextBlock _weekday = TilePieces.Text(TypeRamp.Caption2, InkRole.TextPrimary, FontWeights.SemiBold);
    private readonly TextBlock _day;
    private readonly TextBlock _month = TilePieces.Text(TypeRamp.Caption, InkRole.TextSecondary);
    private readonly TextBlock _week = TilePieces.Text(TypeRamp.Caption2, InkRole.TextTertiary);
    private readonly Grid _monthGrid = new();
    private DateTime _shown = DateTime.MinValue;

    /// <summary>Date affichée (rendus --snapshot) ; null = aujourd'hui.</summary>
    internal DateTime? FixedDate { get; set; }

    public DateFace(TileDefinition definition, TileSize size, TileContext context) : base(definition, size, context)
    {
        // Petit texte : l'accent « texte » (≥ 4,5 sur la tuile, aussi en opale blanche), pas l'accent des icônes (≥ 3).
        _weekday.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentTextKey);
        _day = TilePieces.Number(size == TileSize.S ? TypeRamp.Display : EverydayTileShape.DayHero);
        switch (size)
        {
            case TileSize.S:
                Rows(Auto, Star, Auto);
                Root.Children.Add(_weekday);
                _day.VerticalAlignment = VerticalAlignment.Center;
                _day.Margin = new Thickness(-Space.Px, 0, 0, 0);
                Root.Children.Add(Place(_day, 1));
                Root.Children.Add(Place(_month, 2));
                break;
            case TileSize.W:
            {
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                Root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                _day.VerticalAlignment = VerticalAlignment.Center;
                _day.Margin = new Thickness(Space.S1, 0, 0, 0);
                Root.Children.Add(_day);
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S7, 0, 0, 0) };
                _weekday.FontSize = TypeRamp.Footnote;
                right.Children.Add(_weekday);
                right.Children.Add(_month);
                _week.Margin = new Thickness(0, Space.S1, 0, 0);
                right.Children.Add(_week);
                Root.Children.Add(Place(right, 0, 1));
                break;
            }
            default:
            {
                Rows(Auto, Star);
                var header = new Grid();
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
                header.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
                Numerals.Apply(_day, TypeRamp.Display);
                _day.VerticalAlignment = VerticalAlignment.Center;
                header.Children.Add(_day);
                var right = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S5, 0, 0, 0) };
                right.Children.Add(_weekday);
                right.Children.Add(_month);
                header.Children.Add(Place(right, 0, 1));
                Root.Children.Add(header);
                _monthGrid.Margin = new Thickness(0, Space.S3, 0, 0);
                _monthGrid.VerticalAlignment = VerticalAlignment.Bottom;
                Root.Children.Add(Place(_monthGrid, 1));
                break;
            }
        }
    }

    protected override void Update()
    {
        var today = (FixedDate ?? DateTime.Now).Date;
        if (today == _shown) return;
        _shown = today;
        var culture = Loc.Culture;
        _weekday.Text = Capitalize(today.ToString("dddd", culture), culture);
        if (Small || Large) _weekday.Text = _weekday.Text.ToUpper(culture);
        _day.Text = today.Day.ToString(culture);
        _month.Text = Capitalize(TileSize == TileSize.W ? today.ToString("MMMM yyyy", culture) : today.ToString("MMMM", culture), culture);
        _week.Text = Loc.F("tiles.date.week", ISOWeek.GetWeekOfYear(today));
        if (Large) BuildMonth(today);
    }

    private static string Capitalize(string text, CultureInfo culture) => text.Length == 0 ? text : char.ToUpper(text[0], culture) + text[1..];

    /// <summary>Semaines du mois de <paramref name="date"/> : (ligne, colonne) de chaque jour (calcul pur, --self-test).</summary>
    internal static List<(int Day, int Row, int Column)> MonthCells(DateTime date, DayOfWeek firstDay)
    {
        var first = new DateTime(date.Year, date.Month, 1);
        int offset = ((int)first.DayOfWeek - (int)firstDay + 7) % 7;
        int days = DateTime.DaysInMonth(date.Year, date.Month);
        var cells = new List<(int, int, int)>(days);
        for (int day = 1; day <= days; day++)
        {
            int index = offset + day - 1;
            cells.Add((day, index / 7, index % 7));
        }
        return cells;
    }

    private void BuildMonth(DateTime today)
    {
        _monthGrid.Children.Clear();
        _monthGrid.RowDefinitions.Clear();
        _monthGrid.ColumnDefinitions.Clear();
        var culture = Loc.Culture;
        var firstDay = culture.DateTimeFormat.FirstDayOfWeek;
        var cells = MonthCells(today, firstDay);
        int rows = cells.Max(c => c.Row) + 1;
        for (int c = 0; c < 7; c++) _monthGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        for (int r = 0; r <= rows; r++) _monthGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(EverydayTileShape.MonthCellHeight) });
        // Initiales des jours (« L M M J V S D » / « M T W T F S S »).
        for (int c = 0; c < 7; c++)
        {
            var name = culture.DateTimeFormat.GetShortestDayName((DayOfWeek)(((int)firstDay + c) % 7));
            var label = TilePieces.Text(EverydayTileShape.MonthLabel, InkRole.TextTertiary, FontWeights.SemiBold);
            label.Text = name.Length > 0 ? name[..1].ToUpper(culture) : "";
            label.HorizontalAlignment = HorizontalAlignment.Center;
            label.VerticalAlignment = VerticalAlignment.Center;
            _monthGrid.Children.Add(Place(label, 0, c));
        }
        foreach (var (day, row, column) in cells)
        {
            bool isToday = day == today.Day;
            var number = Numerals.Apply(new TextBlock { Text = day.ToString(culture), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center }, TypeRamp.Caption2);
            number.FontWeight = isToday ? FontWeights.SemiBold : FontWeights.Normal;
            number.SetResourceReference(TextBlock.ForegroundProperty, Paint.Key(isToday ? InkRole.TextPrimary : InkRole.TextSecondary));
            if (isToday)
            {
                var dot = new SquircleBorder { Width = EverydayTileShape.TodayDot, Height = EverydayTileShape.TodayDot, CornerRadius = new CornerRadius(EverydayTileShape.TodayDot / 2), Opacity = EverydayTileShape.TodayDotOpacity };
                dot.SetResourceReference(System.Windows.Controls.Border.BackgroundProperty, AppearanceService.AccentBrushKey);
                _monthGrid.Children.Add(Place(dot, row + 1, column));
            }
            _monthGrid.Children.Add(Place(number, row + 1, column));
        }
    }
}
