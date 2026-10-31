using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Animation;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Feedback;

/// <summary>
/// Écran « Nouveautés » (7C) : montré une fois après chaque mise à jour, avec les notes de la version embarquées dans l'exe
/// (notes/vX.Y.Z.md, et notes/vX.Y.Z.en.md en anglais s'il existe). Sections qui apparaissent l'une après l'autre, logo qui
/// pulse doucement le temps de l'entrée. Revoir : Paramètres → À propos → « Nouveautés de cette version ».
/// </summary>
public sealed partial class WhatsNewWindow : CardWindow
{
    private static WhatsNewWindow? _instance;
    private readonly StackPanel _items = new();

    /// <summary>Une section des notes : titre (« ⚡ Performances ») et ses lignes.</summary>
    internal sealed record Section(string Title, List<string> Lines);

    /// <summary>Notes de cette version (null si l'exe n'en contient pas).</summary>
    internal static string? Notes()
    {
        var assembly = typeof(WhatsNewWindow).Assembly;
        foreach (var name in Loc.Language == "en" ? new[] { "DynamicIsland.WhatsNew.en.md", "DynamicIsland.WhatsNew.fr.md" } : ["DynamicIsland.WhatsNew.fr.md"])
        {
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream != null) return new StreamReader(stream).ReadToEnd();
        }
        return null;
    }

    /// <summary>
    /// Au démarrage (après les modules) : la première fois qu'une nouvelle version tourne, on montre ses nouveautés. Premier
    /// lancement de l'app (assistant de bienvenue) ou exe sans notes : on note juste la version.
    /// </summary>
    public static void ShowAfterUpdate()
    {
        var current = UpdateService.CurrentText;
        var settings = SettingsStore.Current;
        if (SettingsStore.IsReadOnly || settings.Updates.LastSeenVersion == current) return;
        bool show = settings.WelcomeDone && Notes() != null;
        SettingsStore.Update(s => s.Updates.LastSeenVersion = current);
        if (!show) return;
        // Un peu après l'île, et jamais pendant une partie : on attend la fin du jeu.
        var wait = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        wait.Tick += (_, _) =>
        {
            if (GamingMode.IsActive) return;
            wait.Stop();
            Open();
        };
        wait.Start();
    }

    public static void Open()
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }
        _instance = new WhatsNewWindow(snapshot: false);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
    }

    private WhatsNewWindow(bool snapshot) : base(Loc.T("news.title"), CardShape.Width, CardShape.NewsHeight, snapshot)
    {
        var (title, sections) = Parse(Notes() ?? "");
        Body.Children.Add(BuildBody(title, sections));

        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var page = SecondaryButton(Loc.T("news.github"));
        page.HorizontalAlignment = HorizontalAlignment.Left;
        page.Click += (_, _) => OpenReleasePage();
        var done = PrimaryButton(Loc.T("news.ok"));
        done.Click += (_, _) => CloseAnimated();
        Grid.SetColumn(done, 1);
        Footer.Children.Add(page);
        Footer.Children.Add(done);
        Loaded += (_, _) =>
        {
            if (!Snapshot) done.Focus();
        };
    }

    private FrameworkElement BuildBody(string title, List<Section> sections)
    {
        var page = new StackPanel { Margin = new Thickness(Space.S14, Space.S10, Space.S14, 0) };

        var header = new Grid();
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        var logo = new Image { Source = OpalineLogo.Get(CardShape.Logo * 2), Width = CardShape.Logo, Height = CardShape.Logo, RenderTransformOrigin = new Point(0.5, 0.5) };
        if (!Snapshot)
        {
            // Petite pulsation du logo pendant l'entrée (3 fois), puis plus rien : rien ne tourne en continu.
            var pulse = new ScaleTransform(1, 1);
            logo.RenderTransform = pulse;
            var beat = new DoubleAnimation(1, 1.08, Motion.Long) { AutoReverse = true, RepeatBehavior = new RepeatBehavior(3), BeginTime = Motion.Delay, EasingFunction = EaseOut };
            pulse.BeginAnimation(ScaleTransform.ScaleXProperty, beat);
            pulse.BeginAnimation(ScaleTransform.ScaleYProperty, beat);
        }
        var texts = new StackPanel { Margin = new Thickness(Space.S7, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center };
        var overline = new TextBlock { Text = Loc.F("news.version", AppInfo.Name, UpdateService.CurrentText).ToUpperInvariant(), FontSize = TypeRamp.Caption,
                                       FontWeight = FontWeights.SemiBold, Foreground = Paint.TextSecondary };
        texts.Children.Add(overline);
        texts.Children.Add(Heading(title.Length > 0 ? title : Loc.T("news.title")));
        Grid.SetColumn(texts, 1);
        header.Children.Add(logo);
        header.Children.Add(texts);
        page.Children.Add(header);

        if (sections.Count == 0) page.Children.Add(Paragraph(Loc.T("news.empty")));
        foreach (var section in sections)
        {
            _items.Children.Add(new TextBlock
            {
                Text = section.Title,
                FontSize = TypeRamp.Headline,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, Space.S8, 0, Space.S2),
                TextWrapping = TextWrapping.Wrap,
            });
            foreach (var line in section.Lines) _items.Children.Add(Bullet(line));
        }
        page.Children.Add(_items);

        int index = 0;
        Reveal(header, index++);
        foreach (UIElement item in _items.Children) Reveal(item, index++);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    /// <summary>Une nouveauté : pastille d'accent + texte (gras et `code` du Markdown gardés).</summary>
    private static Grid Bullet(string line)
    {
        var grid = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S1) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var dot = new SquircleBorder
        {
            Width = CardShape.Bullet, Height = CardShape.Bullet, CornerRadius = Corners.Pill, VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(Space.S1, Space.S3, Space.S5, 0),
        };
        dot.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        var text = new TextBlock { FontSize = TypeRamp.Body, TextWrapping = TextWrapping.Wrap, Foreground = Paint.TextPrimary };
        Inlines(line, text.Inlines);
        Grid.SetColumn(text, 1);
        grid.Children.Add(dot);
        grid.Children.Add(text);
        return grid;
    }

    [GeneratedRegex(@"\*\*(.+?)\*\*|`(.+?)`|\[(.+?)\]\((.+?)\)")]
    private static partial Regex Marks();

    private static void Inlines(string line, InlineCollection inlines)
    {
        // Guillemets français : espaces insécables, pour qu'un « » » ne se retrouve jamais seul en début de ligne.
        line = line.Replace("« ", "«\u00A0").Replace(" »", "\u00A0»");
        int at = 0;
        foreach (Match match in Marks().Matches(line))
        {
            if (match.Index > at) inlines.Add(new Run(line[at..match.Index]));
            if (match.Groups[1].Success) inlines.Add(new Bold(new Run(match.Groups[1].Value)));
            else if (match.Groups[2].Success) inlines.Add(new Run(match.Groups[2].Value) { FontFamily = FontStack.Mono });
            else inlines.Add(new Run(match.Groups[3].Value)); // lien : seulement son texte
            at = match.Index + match.Length;
        }
        if (at < line.Length) inlines.Add(new Run(line[at..]));
    }

    /// <summary>
    /// Notes Markdown → titre (« ## … » : « Opaline 1.24.0 — … », seulement la partie après le tiret) et sections (« ### … »,
    /// puces « - … » et paragraphes). Ce qui précède la première section (lien de téléchargement) et les lignes qui parlent du
    /// fichier à télécharger sont ignorés : l'app est déjà installée.
    /// </summary>
    internal static (string Title, List<Section> Sections) Parse(string markdown)
    {
        string title = "";
        var sections = new List<Section>();
        foreach (var raw in markdown.Replace("\r\n", "\n").Split('\n'))
        {
            var line = raw.Trim();
            if (line.StartsWith("### "))
            {
                sections.Add(new Section(line[4..].Trim(), []));
                continue;
            }
            if (line.StartsWith("## "))
            {
                var heading = line[3..].Trim();
                int dash = heading.IndexOfAny(['—', '–']);
                title = dash >= 0 ? heading[(dash + 1)..].Trim() : heading;
                if (title.Length > 0) title = char.ToUpper(title[0], Loc.Culture) + title[1..];
                continue;
            }
            if (sections.Count == 0 || line.Length == 0) continue;
            if (line.Contains("Opaline-Setup.exe", StringComparison.OrdinalIgnoreCase)) continue;
            var text = line.StartsWith("- ") || line.StartsWith("* ") ? line[2..].Trim() : line;
            if (raw.StartsWith("  ") && sections[^1].Lines.Count > 0) sections[^1].Lines[^1] += " " + text; // suite de la puce
            else sections[^1].Lines.Add(text);
        }
        return (title, sections.Where(s => s.Lines.Count > 0).ToList());
    }

    private static void OpenReleasePage()
    {
        try
        {
            var repository = SettingsStore.Current.Updates.Repository;
            Process.Start(new ProcessStartInfo($"https://github.com/{repository}/releases/tag/v{UpdateService.CurrentText}") { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            Log.Error("Nouveautés : page GitHub", ex);
        }
    }

    // ------------------------------------------------------------------ --snapshot

    internal static async Task RenderSnapshotAsync(string folder)
    {
        var window = new WhatsNewWindow(snapshot: true);
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "15-nouveautes.png"));
        window.Close();
    }
}
