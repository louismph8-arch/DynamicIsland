using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Dock;

/// <summary>
/// Recherche façon Spotlight, ouverte par le bouton « Rechercher » du dock : applis installées,
/// fichiers (recherche de l'Explorateur) et web. ↑/↓ pour choisir, Entrée pour lancer, Échap pour fermer.
/// </summary>
public partial class SearchWindow : Window
{
    private const int MaxApps = 6;
    private static readonly Brush SelectedBrush = Paint.Selected;
    private static SearchWindow? _instance;

    private readonly List<(SquircleBorder Row, Action Run)> _rows = [];
    private int _selected;

    private SearchWindow()
    {
        InitializeComponent();
        SourceInitialized += (_, _) =>
        {
            // Pas dans Alt+Tab ni la barre des tâches, mais activable (on doit pouvoir taper).
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, (GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW);
        };
        Deactivated += (_, _) => Hide(); // clic ailleurs = fermeture
    }

    /// <summary>Ouvre la recherche, ou la ferme si elle est déjà ouverte.</summary>
    public static void Toggle()
    {
        _instance ??= new SearchWindow();
        if (_instance.IsVisible) _instance.Hide();
        else _instance.Open();
    }

    private async void Open()
    {
        QueryBox.Text = "";
        PositionOnPrimaryScreen();
        Show();
        Activate();
        QueryBox.Focus();
        Keyboard.Focus(QueryBox);

        await AppCatalog.RefreshAsync(); // rapide après le premier chargement (cache de 5 min)
        if (IsVisible) UpdateResults();
    }

    /// <summary>Centré horizontalement, au premier cinquième de la hauteur de l'écran principal.</summary>
    private void PositionOnPrimaryScreen()
    {
        var (bounds, _, scale) = ScreenInfo.Primary();
        Left = bounds.Left / scale + ((bounds.Right - bounds.Left) / scale - Width) / 2;
        Top = bounds.Top / scale + (bounds.Bottom - bounds.Top) / scale * 0.2;
    }

    // ------------------------------------------------------------------ résultats

    private void QueryBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateResults();
    }

    private void UpdateResults()
    {
        _rows.Clear();
        Results.Children.Clear();
        var query = QueryBox.Text.Trim();
        bool hasQuery = query.Length > 0;
        Divider.Visibility = Results.Visibility = hasQuery ? Visibility.Visible : Visibility.Collapsed;
        if (!hasQuery) return;

        foreach (var app in AppCatalog.Search(query, MaxApps))
            AddRow(IconService.Get(app.ParsingName), null, app.Name, Loc.T("search.kind.app"), app.Launch);

        AddRow(null, Icons.Search, Loc.F("search.files", query), Loc.T("search.kind.explorer"), () => SearchFiles(query));
        AddRow(null, Icons.Globe, Loc.F("search.web", query), "Google", () => SearchWeb(query));
        Select(0);
    }

    private void AddRow(ImageSource? icon, string? glyph, string title, string subtitle, Action run)
    {
        int index = _rows.Count;
        UIElement iconElement = icon != null
            ? new Image { Source = icon, Width = Sizes.GlyphTile, Height = Sizes.GlyphTile }
            : new SquircleBorder
            {
                Width = Sizes.GlyphTile,
                Height = Sizes.GlyphTile,
                CornerRadius = Corners.S,
                Background = Paint.SurfaceHover,
                Child = new TextBlock
                {
                    Text = glyph,
                    FontFamily = FontStack.Icons,
                    FontSize = IconSize.L,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center,
                },
            };
        RenderOptions.SetBitmapScalingMode(iconElement, BitmapScalingMode.HighQuality);

        var texts = new StackPanel { Margin = new Thickness(Space.S6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock { Text = title, FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, TextTrimming = TextTrimming.CharacterEllipsis });
        texts.Children.Add(new TextBlock { Text = subtitle, FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary });

        var content = new Grid();
        content.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        content.ColumnDefinitions.Add(new ColumnDefinition());
        Grid.SetColumn(texts, 1);
        content.Children.Add(iconElement);
        content.Children.Add(texts);

        var row = new SquircleBorder
        {
            Child = content,
            CornerRadius = Corners.L,
            Padding = new Thickness(Space.S5, Space.S4, Space.S5, Space.S4),
            Background = Brushes.Transparent,
            Cursor = Cursors.Hand,
        };
        row.MouseEnter += (_, _) => Select(index);
        row.MouseLeftButtonUp += (_, _) => Run(index);

        Results.Children.Add(row);
        _rows.Add((row, run));
    }

    private void Select(int index)
    {
        if (_rows.Count == 0) return;
        _selected = Math.Clamp(index, 0, _rows.Count - 1);
        for (int i = 0; i < _rows.Count; i++)
            _rows[i].Row.Background = i == _selected ? SelectedBrush : Brushes.Transparent;
    }

    private void Run(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var run = _rows[index].Run;
        Hide();
        run();
    }

    private void QueryBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down:
                Select(_selected + 1);
                break;
            case Key.Up:
                Select(_selected - 1);
                break;
            case Key.Enter:
                Run(_selected);
                break;
            case Key.Escape:
                Hide();
                break;
            default:
                return;
        }
        e.Handled = true;
    }

    // ------------------------------------------------------------------ actions

    internal static void SearchFiles(string query)
    {
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        Open($"search-ms:query={Uri.EscapeDataString(query)}&crumb=location:{Uri.EscapeDataString(home)}");
    }

    internal static void SearchWeb(string query) =>
        Open("https://www.google.com/search?q=" + Uri.EscapeDataString(query));

    private static void Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error($"Recherche : ouverture de « {target} »", ex);
        }
    }

    // ------------------------------------------------------------------ mode --snapshot

    /// <summary>Rend la recherche (avec une requête d'exemple) dans un PNG, hors écran.</summary>
    internal static async Task RenderSnapshotAsync(string path, string query)
    {
        var window = new SearchWindow { ShowActivated = false, Left = -30000, Top = 0 };
        window.Show();
        await AppCatalog.RefreshAsync();
        window.QueryBox.Text = query;
        window.UpdateResults();
        await SnapshotRenderer.SaveAsync(window, path);
        window.Close();
    }
}
