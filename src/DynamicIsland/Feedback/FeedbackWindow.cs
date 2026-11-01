using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Services.Friends;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Feedback;

/// <summary>
/// « Donner mon avis / Signaler un bug » (7C) : avis ou bug, texte, capture de l'écran et rapport de bug facultatifs, envoyés au
/// serveur « Amis en ligne » qui les poste dans un salon Discord (webhook gardé secret sur Cloudflare). RIEN n'est envoyé avant
/// le clic sur « Envoyer » ; la capture est prise à l'ouverture (avant que la fenêtre n'apparaisse) et montrée en aperçu.
/// </summary>
public sealed class FeedbackWindow : CardWindow
{
    public const int MaxText = 1800; // le serveur coupe au-delà

    private static FeedbackWindow? _instance;

    private readonly RadioButton _idea, _bug;
    private readonly TextBox _text = new();
    private readonly TextBlock _counter = new() { FontSize = TypeRamp.Caption, Foreground = Paint.TextTertiary, HorizontalAlignment = HorizontalAlignment.Right };
    private readonly CheckBox _screenshotToggle, _reportToggle;
    private readonly Image _thumbnail = new() { Width = CardShape.Thumbnail, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Left };
    private readonly SquircleBorder _thumbnailFrame = new() { CornerRadius = Corners.S, Margin = new Thickness(0, Space.S4, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
    private readonly SquircleBorder _unavailable = new();
    private readonly TextBlock _unavailableText = new() { TextWrapping = TextWrapping.Wrap, FontSize = TypeRamp.Body };
    private readonly Button _openConnected;
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _send, _cancel;
    private readonly byte[]? _screenshot;
    private FriendsAccount? _account;
    private bool _reportTouched, _busy, _ready;

    /// <summary>Ouvre le formulaire (ou le ramène devant). La capture est prise avant qu'il apparaisse.</summary>
    public static void Open()
    {
        if (_instance != null)
        {
            _instance.Activate();
            return;
        }
        _instance = new FeedbackWindow(CaptureScreen(), snapshot: false);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
        _instance.Activate();
        _ = _instance.CheckServerAsync();
    }

    private FeedbackWindow(byte[]? screenshot, bool snapshot)
        : base(Loc.T("feedback.title"), CardShape.Width, CardShape.FeedbackHeight, snapshot)
    {
        _screenshot = screenshot;
        _idea = new RadioButton { Style = (Style)FindResource("OptionCard"), GroupName = "FeedbackKind", IsChecked = true, Margin = new Thickness(0, 0, Space.S3, 0),
                                  Content = Loc.T("feedback.kind.idea"), Tag = Loc.T("feedback.kind.idea.desc") };
        _bug = new RadioButton { Style = (Style)FindResource("OptionCard"), GroupName = "FeedbackKind", Margin = new Thickness(Space.S3, 0, 0, 0),
                                 Content = Loc.T("feedback.kind.bug"), Tag = Loc.T("feedback.kind.bug.desc") };
        _bug.Checked += (_, _) =>
        {
            if (!_reportTouched) _reportToggle!.IsChecked = true; // un bug : le rapport aide beaucoup (décochable)
        };
        _idea.Checked += (_, _) =>
        {
            if (!_reportTouched) _reportToggle!.IsChecked = false;
        };

        _screenshotToggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = false, IsEnabled = screenshot != null };
        _screenshotToggle.Click += (_, _) => ShowThumbnail();
        _reportToggle = new CheckBox { Style = (Style)FindResource("SwitchToggle"), IsChecked = false };
        _reportToggle.Click += (_, _) => _reportTouched = true;

        _send = PrimaryButton(Loc.T("feedback.send"));
        _send.Click += async (_, _) => await SendAsync();
        _cancel = SecondaryButton(Loc.T("feedback.cancel"));
        _cancel.Click += (_, _) => CloseAnimated();
        _openConnected = SecondaryButton(Loc.T("feedback.unavailable.open"));
        _openConnected.Margin = new Thickness(0, Space.S4, 0, 0);
        _openConnected.HorizontalAlignment = HorizontalAlignment.Left;
        _openConnected.Click += (_, _) =>
        {
            SettingsWindow.Open(SettingsPage.Friends);
            CloseAnimated();
        };

        Body.Children.Add(BuildBody());
        BuildFooter();
        ShowThumbnail();
        UpdateCounter();
        SetReady(false, Loc.T("feedback.status.checking"));
        Loaded += (_, _) =>
        {
            if (!Snapshot) _text.Focus();
        };
    }

    // ------------------------------------------------------------------ mise en page

    private FrameworkElement BuildBody()
    {
        var page = new StackPanel { Margin = new Thickness(Space.S14, Space.S10, Space.S14, 0) };

        var title = new StackPanel { Orientation = Orientation.Horizontal };
        var badge = new SquircleBorder
        {
            Width = WelcomeShape.TipIcon, Height = WelcomeShape.TipIcon, CornerRadius = Corners.Pill, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = Icons.Feedback, FontFamily = FontStack.Icons, FontSize = IconSize.L, Foreground = Paint.White,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        badge.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        var heading = Heading(Loc.T("feedback.title"));
        heading.Margin = new Thickness(Space.S6, 0, 0, 0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(badge);
        title.Children.Add(heading);
        page.Children.Add(title);
        page.Children.Add(Paragraph(Loc.T("feedback.intro")));

        var kinds = new System.Windows.Controls.Primitives.UniformGrid { Columns = 2, Margin = new Thickness(0, Space.S6, 0, 0) };
        kinds.Children.Add(_idea);
        kinds.Children.Add(_bug);
        page.Children.Add(kinds);

        _text.AcceptsReturn = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.MaxLength = MaxText;
        _text.Height = CardShape.TextHeight;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.Background = Brushes.Transparent;
        _text.BorderThickness = new Thickness(0);
        _text.Foreground = Paint.TextPrimary;
        _text.CaretBrush = Paint.White;
        _text.FontSize = TypeRamp.Body;
        _text.TextChanged += (_, _) => UpdateCounter();
        var placeholder = new TextBlock { Text = Loc.T("feedback.placeholder"), FontSize = TypeRamp.Body, Foreground = Paint.TextTertiary,
                                          IsHitTestVisible = false, Margin = new Thickness(Space.S1, 0, 0, 0), TextWrapping = TextWrapping.Wrap };
        _text.TextChanged += (_, _) => placeholder.Visibility = _text.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var box = new SquircleBorder
        {
            Background = Paint.Control, BorderBrush = Paint.Selected, BorderThickness = Borders.Hairline, CornerRadius = Corners.M,
            Padding = new Thickness(Space.S5, Space.S4, Space.S5, Space.S4), Margin = new Thickness(0, Space.S6, 0, 0),
            Child = new Grid { Children = { placeholder, _text } },
        };
        _text.GotKeyboardFocus += (_, _) => box.SetResourceReference(Border.BorderBrushProperty, AppearanceService.AccentBrushKey);
        _text.LostKeyboardFocus += (_, _) => box.BorderBrush = Paint.Selected;
        page.Children.Add(box);
        _counter.Margin = new Thickness(0, Space.S2, 0, 0);
        page.Children.Add(_counter);

        var screenshotRow = Row(Loc.T("feedback.screenshot"), Loc.T(_screenshot != null ? "feedback.screenshot.desc" : "feedback.screenshot.none"), _screenshotToggle);
        _thumbnailFrame.Child = _thumbnail;
        ((StackPanel)((Grid)screenshotRow.Child).Children[0]).Children.Add(_thumbnailFrame);
        page.Children.Add(screenshotRow);
        page.Children.Add(Row(Loc.T("feedback.report"), Loc.T("feedback.report.desc"), _reportToggle));

        _unavailable.Style = (Style)FindResource("SettingCard");
        _unavailable.Margin = new Thickness(0, Space.S6, 0, 0);
        _unavailable.Child = new StackPanel { Children = { _unavailableText, _openConnected } };
        _unavailable.Visibility = Visibility.Collapsed;
        page.Children.Add(_unavailable);

        int index = 0;
        foreach (UIElement child in page.Children) Reveal(child, index++);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private void BuildFooter()
    {
        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        _cancel.Margin = new Thickness(0, 0, Space.S4, 0);
        buttons.Children.Add(_cancel);
        buttons.Children.Add(_send);
        Grid.SetColumn(buttons, 1);
        _status.Margin = new Thickness(0, 0, Space.S6, 0);
        Footer.Children.Add(_status);
        Footer.Children.Add(buttons);
    }

    /// <summary>Carte : titre + description à gauche, interrupteur à droite (comme dans les paramètres).</summary>
    private SquircleBorder Row(string title, string description, CheckBox toggle)
    {
        var texts = new StackPanel();
        texts.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("CardTitle") });
        texts.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("CardDescription"), TextWrapping = TextWrapping.Wrap });
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.Children.Add(texts);
        Grid.SetColumn(toggle, 1);
        toggle.VerticalAlignment = VerticalAlignment.Top;
        toggle.Margin = new Thickness(Space.S6, Space.S1, 0, 0);
        grid.Children.Add(toggle);
        return new SquircleBorder { Style = (Style)FindResource("SettingCard"), Child = grid, Margin = new Thickness(0, Space.S4, 0, 0) };
    }

    private void UpdateCounter() => _counter.Text = $"{_text.Text.Length} / {MaxText}";

    private void ShowThumbnail()
    {
        bool show = _screenshotToggle.IsChecked == true && _screenshot != null;
        _thumbnailFrame.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (show && _thumbnail.Source == null) _thumbnail.Source = Decode(_screenshot!);
    }

    private static BitmapImage Decode(byte[] jpeg)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.DecodePixelWidth = (int)(CardShape.Thumbnail * 2);
        image.StreamSource = new MemoryStream(jpeg);
        image.EndInit();
        image.Freeze();
        return image;
    }

    // ------------------------------------------------------------------ serveur

    /// <summary>Compte « Amis en ligne » et serveur qui accepte les avis ? Sinon on explique quoi faire.</summary>
    private async Task CheckServerAsync()
    {
        var settings = SettingsStore.Current.Friends;
        _account = settings.Enabled ? FriendsAccount.Load() : null;
        if (_account == null)
        {
            ShowUnavailable(Loc.T("feedback.unavailable.friends"), openSettings: true);
            return;
        }
        var (ok, feedback) = await FriendsApi.InfoAsync(_account.Server);
        if (!ok) ShowUnavailable(Loc.T("feedback.unavailable.offline"), openSettings: false);
        else if (!feedback) ShowUnavailable(Loc.T("feedback.unavailable.off"), openSettings: false);
        else SetReady(true, Loc.T("feedback.status.ready"));
    }

    private void ShowUnavailable(string text, bool openSettings)
    {
        _unavailableText.Text = text;
        _openConnected.Visibility = openSettings ? Visibility.Visible : Visibility.Collapsed;
        _unavailable.Visibility = Visibility.Visible;
        SetReady(false, Loc.T("feedback.status.unavailable"));
    }

    private void SetReady(bool ready, string status)
    {
        _ready = ready;
        _status.Text = status;
        _send.IsEnabled = ready && !_busy;
    }

    private async Task SendAsync()
    {
        if (!_ready || _busy || _account == null) return;
        var text = _text.Text.Trim();
        if (text.Length == 0)
        {
            _status.Text = Loc.T("feedback.status.empty");
            _text.Focus();
            return;
        }
        _busy = true;
        _send.IsEnabled = false;
        _status.Text = Loc.T("feedback.status.sending");
        string? report = null;
        try
        {
            if (_reportToggle.IsChecked == true) report = BugReport.Create(Path.Combine(Path.GetTempPath(), "Opaline-avis"));
            var error = await FriendsApi.SendFeedbackAsync(_account, _bug.IsChecked == true, text, UpdateService.CurrentText,
                                                           _screenshotToggle.IsChecked == true ? _screenshot : null, report);
            if (error == null)
            {
                Log.Info($"Avis envoyé ({(_bug.IsChecked == true ? "bug" : "avis")}, capture {(_screenshotToggle.IsChecked == true ? "oui" : "non")}, rapport {(report != null ? "oui" : "non")})");
                _status.Text = Loc.T("feedback.status.sent");
                await Task.Delay(1400);
                CloseAnimated();
                return;
            }
            Log.Info($"Avis refusé par le serveur : {error}");
            var code = error is "feedback_off" or "too_many" or "too_big" or "auth" or "offline" or "discord" ? error : "server";
            _status.Text = Loc.T($"feedback.error.{code}");
        }
        catch (Exception ex)
        {
            Log.Error("Avis : envoi", ex);
            _status.Text = Loc.T("feedback.error.server");
        }
        finally
        {
            if (report != null)
                try
                {
                    File.Delete(report);
                }
                catch (IOException)
                {
                    // le dossier temporaire sera nettoyé plus tard
                }
            _busy = false;
            _send.IsEnabled = _ready;
        }
    }

    /// <summary>L'écran où se trouve la souris, en JPEG (1280 px de large au plus), avant que la fenêtre n'apparaisse.</summary>
    private static byte[]? CaptureScreen()
    {
        try
        {
            var cursor = System.Windows.Forms.Cursor.Position;
            var screens = Services.Remote.RemoteScreen.Screens;
            int index = -1;
            for (int i = 0; i < screens.Count; i++)
                if (screens[i].Contains(cursor)) index = i;
            return Services.Remote.RemoteScreen.Capture(index);
        }
        catch (Exception ex)
        {
            Log.Error("Avis : capture de l'écran", ex);
            return null;
        }
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : formulaire rempli (exemple), capture remplacée par un fond de l'app (rien du vrai écran).</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var art = WallpaperArt.Render(WallpaperDesign.Aurore, Wallpapers.Night, 640, 360);
        var encoder = new JpegBitmapEncoder { QualityLevel = 80 };
        encoder.Frames.Add(BitmapFrame.Create(art));
        using var stream = new MemoryStream();
        encoder.Save(stream);
        var window = new FeedbackWindow(stream.ToArray(), snapshot: true);
        window._bug.IsChecked = true;
        window._text.Text = Loc.T("feedback.sample");
        window._screenshotToggle.IsChecked = true;
        window.ShowThumbnail();
        window.SetReady(true, Loc.T("feedback.status.ready"));
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "14-avis.png"));
        window.Close();
    }
}
