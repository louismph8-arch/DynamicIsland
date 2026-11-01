using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Traduire » de l'île (demandé le 2026-11-01) : en haut « Anglais → Français », « ⇄ » et la langue cible (menu) ; au
/// milieu le texte d'origine (ce qui a été copié) et la traduction qui s'écrit au fil de l'eau (chacun défile à la molette) ;
/// en bas « Traduire la page », « Arrêter » et « Traduire ce que j'ai copié ». L'île ne prend jamais le clavier : le texte vient
/// du presse-papiers, de la palette (« tr … ») ou du raccourci. Ouvrir l'onglet ne traduit RIEN tout seul (aucune consommation
/// cachée). Vue autonome (réutilisable telle quelle comme tuile en O6). Voir <see cref="TranslateService"/>.
/// </summary>
public sealed class TranslateView : UserControl
{
    private readonly TextBlock _languages = new() { FontSize = TypeRamp.Footnote, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, Visibility = Visibility.Collapsed };
    private readonly Button _target, _swap, _copy, _page, _stop, _translate;
    private readonly TextBlock _targetLabel = new() { FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _source = new() { FontSize = TypeRamp.Footnote, TextWrapping = TextWrapping.Wrap, Foreground = Paint.TextSecondary, LineHeight = TypeRamp.Footnote * 1.45 };
    private readonly StackPanel _result = new();
    private readonly ScrollViewer _sourceScroller, _resultScroller;
    private readonly DispatcherTimer _dots = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(7) };
    private int _dotCount;
    private bool _active, _dirty = true, _sample, _followBottom = true;

    /// <summary>« Traduire ce que j'ai copié » (l'île lit le presse-papiers : elle sait aussi refuser un mot de passe copié).</summary>
    public event Action? TranslateClipboardRequested;

    public TranslateView()
    {
        Width = IslandShape.TranslateContentWidth;
        Height = IslandShape.TranslateContentHeight;

        // ---- en haut : langues, ⇄, langue cible
        _swap = new Button { Style = (Style)FindResource("SmallIconButton"), Content = Icons.SwapLanguages, ToolTip = Loc.T("translate.view.swap"), Margin = new Thickness(Space.S3, 0, Space.S2, 0) };
        _swap.Click += (_, _) =>
        {
            if (!_sample) TranslateService.Swap();
        };
        _target = new Button { Style = (Style)FindResource("ChipButton"), ToolTip = Loc.T("translate.view.target.tip") };
        var targetContent = new StackPanel { Orientation = Orientation.Horizontal };
        targetContent.Children.Add(new TextBlock { Text = Icons.Translate, FontFamily = FontStack.Icons, FontSize = IconSize.S, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, Space.Px, Space.S3, 0) });
        targetContent.Children.Add(_targetLabel);
        targetContent.Children.Add(new TextBlock { Text = Icons.ChevronDown, FontFamily = FontStack.Icons, FontSize = IconSize.XS, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S2, Space.Px, 0, 0), Foreground = Paint.TextTertiary });
        _target.Content = targetContent;
        _target.Click += (_, _) => ShowTargetMenu();

        var header = new Grid { Margin = new Thickness(Space.S2, 0, 0, Space.S3) };
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.Children.Add(_languages);
        header.Children.Add(_status);
        Grid.SetColumn(_swap, 1);
        Grid.SetColumn(_target, 2);
        header.Children.Add(_swap);
        header.Children.Add(_target);

        // ---- au milieu : texte d'origine | traduction
        _sourceScroller = Scroller(_source);
        _resultScroller = Scroller(_result);
        _resultScroller.ScrollChanged += (_, e) =>
        {
            if (e.ExtentHeightChange == 0) _followBottom = _resultScroller.VerticalOffset >= _resultScroller.ScrollableHeight - 4;
        };
        _copy = new Button { Style = (Style)FindResource("SmallIconButton"), Content = Icons.Copy, ToolTip = Loc.T("translate.view.copy"), Visibility = Visibility.Collapsed };
        _copy.Click += (_, _) => CopyTranslation();
        var panes = new Grid();
        panes.ColumnDefinitions.Add(new ColumnDefinition());
        panes.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandShape.TranslatePaneGap) });
        panes.ColumnDefinitions.Add(new ColumnDefinition());
        var origin = Pane(Loc.T("translate.view.original"), _sourceScroller, null);
        var translation = Pane(Loc.T("translate.view.translation"), _resultScroller, _copy);
        Grid.SetColumn(translation, 2);
        panes.Children.Add(origin);
        panes.Children.Add(translation);

        // ---- en bas : page web, arrêter, traduire ce que j'ai copié
        _page = TextButton(Loc.T("translate.view.page"), Icons.Globe);
        _page.ToolTip = Loc.T("translate.view.page.tip");
        _page.Click += async (_, _) =>
        {
            if (_sample) return;
            _page.IsEnabled = false;
            try
            {
                var problem = await TranslateService.TranslateOpenPageAsync();
                ShowStatus(problem ?? Loc.T("translate.page.opened"));
            }
            finally
            {
                _page.IsEnabled = true;
            }
        };
        _stop = TextButton(Loc.T("translate.view.stop"), Icons.Stop);
        _stop.Click += (_, _) => TranslateService.Stop();
        _stop.Margin = new Thickness(Space.S3, 0, 0, 0);
        _translate = new Button { Style = (Style)FindResource("RoundButton"), Height = Sizes.Chip + Space.S4, Padding = new Thickness(Space.S6, 0, Space.S6, 0), Foreground = Paint.White, Margin = new Thickness(Space.S3, 0, 0, 0) };
        _translate.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        _translate.Content = Label(Icons.Paste, Loc.T("translate.view.clipboard"));
        _translate.ToolTip = Loc.F("translate.view.clipboard.tip", Hotkeys.Display(SettingsStore.Current.Translate.Hotkey));
        _translate.Click += (_, _) =>
        {
            if (!_sample) TranslateClipboardRequested?.Invoke();
        };
        var bar = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, Space.S4, 0, 0) };
        bar.Children.Add(_page);
        bar.Children.Add(_stop);
        bar.Children.Add(_translate);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(panes, 1);
        Grid.SetRow(bar, 2);
        layout.Children.Add(header);
        layout.Children.Add(panes);
        layout.Children.Add(bar);
        Content = layout;

        _dots.Tick += (_, _) =>
        {
            _dotCount = (_dotCount + 1) % 4;
            if (TranslateService.Current is { Done: false } job && job.Translation.Length == 0) Refresh();
        };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Visibility = Visibility.Collapsed;
            _languages.Visibility = Visibility.Visible;
        };
        TranslateService.Changed += () =>
        {
            if (_sample) return;
            _dirty = true;
            if (_active) Refresh();
        };
        SettingsStore.Changed += () =>
        {
            _dirty = true;
            if (_active) Refresh();
        };
    }

    /// <summary>Vrai quand l'onglet est affiché (rien n'est redessiné ni demandé sinon).</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (value && _dirty) Refresh();
            if (!value) _dots.Stop();
        }
    }

    /// <summary>Message bref à la place des langues (page ouverte, page non traduisible, presse-papiers vide…).</summary>
    public void ShowStatus(string text)
    {
        _status.Text = text;
        _status.ToolTip = text;
        _status.Visibility = Visibility.Visible;
        _languages.Visibility = Visibility.Collapsed;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void Refresh()
    {
        _dirty = false;
        var job = TranslateService.Current;
        _targetLabel.Text = TranslateService.NameOf(TranslateService.TargetCode);
        var source = job?.Source;
        _languages.Text = Loc.F("translate.view.languages", source != null ? TranslateService.NameOf(source) : Loc.T("translate.view.detect"),
            TranslateService.NameOf(job?.Target ?? TranslateService.TargetCode));
        bool busy = !_sample && TranslateService.IsBusy;
        _stop.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _translate.ToolTip = Loc.F("translate.view.clipboard.tip", Hotkeys.Display(SettingsStore.Current.Translate.Hotkey));

        // Texte d'origine
        if (job == null || job.Text.Length == 0)
        {
            _source.Text = Loc.T("translate.view.original.empty");
            _source.Foreground = Paint.TextTertiary;
        }
        else
        {
            _source.Text = job.Text;
            _source.Foreground = Paint.TextSecondary;
        }

        // Traduction
        _result.Children.Clear();
        _copy.Visibility = job is { Done: true, Error: null } && job.Translation.Length > 0 ? Visibility.Visible : Visibility.Collapsed;
        if (job == null)
        {
            _result.Children.Add(new TextBlock
            {
                Text = Loc.F("translate.view.hint", Hotkeys.Display(SettingsStore.Current.Palette.Hotkey), ClaudeService.AssistantName),
                FontSize = TypeRamp.Footnote, Foreground = Paint.TextTertiary, TextWrapping = TextWrapping.Wrap, LineHeight = TypeRamp.Footnote * 1.45,
            });
        }
        else
        {
            var text = new TextBlock { FontSize = TypeRamp.Body, TextWrapping = TextWrapping.Wrap, LineHeight = TypeRamp.Body * 1.45, Foreground = Paint.TextPrimary };
            if (job.Translation.Length > 0) ClaudeView.Markdown(job.Translation, text.Inlines);
            else if (!job.Done)
            {
                text.Text = Loc.F("translate.view.working", ClaudeService.AssistantName) + new string('.', _dotCount);
                text.Foreground = Paint.TextSecondary;
            }
            if (text.Inlines.Count > 0) _result.Children.Add(text);
            if (job.Error is { } error)
            {
                _result.Children.Add(new TextBlock { Text = error, FontSize = TypeRamp.Footnote, Foreground = Paint.Danger, TextWrapping = TextWrapping.Wrap });
                if (job.Fix != ClaudeFix.None)
                {
                    var (fixText, fixGlyph) = ClaudeView.FixLabel(job.Fix);
                    var fix = TextButton(fixText, fixGlyph);
                    fix.HorizontalAlignment = HorizontalAlignment.Left;
                    fix.Margin = new Thickness(-Space.S3, Space.S3, 0, 0);
                    var which = job.Fix;
                    fix.Click += (_, _) => ClaudeView.RunFix(which);
                    _result.Children.Add(fix);
                }
            }
        }
        if (busy && job is { Done: false } && job.Translation.Length == 0) _dots.Start();
        else _dots.Stop();
        if (job is { Done: false } && _followBottom) _resultScroller.Dispatcher.BeginInvoke(_resultScroller.ScrollToEnd, DispatcherPriority.Loaded);
        if (job == null || job.Raw.Length == 0) _followBottom = true;
    }

    private void CopyTranslation()
    {
        if (TranslateService.Current is not { Done: true } job || job.Translation.Length == 0 || _sample) return;
        try
        {
            Clipboard.SetText(job.Translation);
            ShowStatus(Loc.T("translate.view.copied"));
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ShowStatus(Loc.T("clipboard.copy.failed")); // presse-papiers occupé par une autre appli
        }
    }

    /// <summary>Menu des langues (menu sombre / clair d'Opaline), la langue cible cochée.</summary>
    private void ShowTargetMenu()
    {
        var menu = new ContextMenu { PlacementTarget = _target, Placement = PlacementMode.Bottom };
        var current = TranslateService.TargetCode;
        foreach (var language in TranslateService.Languages)
        {
            var item = new MenuItem { Header = language.Name, IsCheckable = true, IsChecked = language.Code == current };
            var code = language.Code;
            item.Click += (_, _) =>
            {
                if (!_sample) TranslateService.SetTarget(code);
            };
            menu.Items.Add(item);
        }
        menu.IsOpen = true;
    }

    // ------------------------------------------------------------------ petits éléments

    private static ScrollViewer Scroller(UIElement content)
    {
        var scroller = new ScrollViewer
        {
            Content = content,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
        };
        if (Application.Current?.TryFindResource("ThinScrollBar") is Style thin) scroller.Resources.Add(typeof(ScrollBar), thin);
        return scroller;
    }

    /// <summary>Cadre en verre opalin : petit titre (+ bouton à droite), puis le texte qui défile.</summary>
    private static FrameworkElement Pane(string title, ScrollViewer scroller, Button? action)
    {
        var top = new Grid { Margin = new Thickness(0, 0, 0, Space.S2), MinHeight = Sizes.Chip };
        top.ColumnDefinitions.Add(new ColumnDefinition());
        top.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        top.Children.Add(new TextBlock { Text = title, FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, Foreground = Paint.TextTertiary, VerticalAlignment = VerticalAlignment.Center });
        if (action != null)
        {
            Grid.SetColumn(action, 1);
            top.Children.Add(action);
        }
        var panel = new DockPanel { LastChildFill = true };
        DockPanel.SetDock(top, System.Windows.Controls.Dock.Top);
        panel.Children.Add(top);
        panel.Children.Add(scroller);
        return Design.OpalTile.Apply(new SquircleBorder
        {
            CornerRadius = Corners.M,
            Padding = new Thickness(Space.S5, Space.S3, Space.S3, Space.S4),
            Child = panel,
        }, glow: false);
    }

    private Button TextButton(string text, string glyph) => new()
    {
        Style = (Style)FindResource("ChipButton"),
        Content = Label(glyph, text),
        Foreground = Paint.TextSecondary,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private static StackPanel Label(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.S, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, Space.Px, Space.S3, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Traduction d'exemple écrite à la main pour les rendus (aucune requête, rien n'est demandé à l'assistant).</summary>
    internal void ShowSample()
    {
        var english = Loc.Language == Loc.English;
        var job = english
            ? new TranslateJob("Bonjour à tous ! La réunion de demain est déplacée à 14 h.\n\n- Apportez vos notes\n- La salle change : B204", "en")
            {
                Raw = "[[fr]]\nHi everyone! Tomorrow's meeting has been moved to 2 p.m.\n\n- Bring your notes\n- The room changes: B204",
                Done = true,
            }
            : new TranslateJob("Hi everyone! Tomorrow's meeting has been moved to 2 p.m.\n\n- Bring your notes\n- The room changes: B204", "fr")
            {
                Raw = "[[en]]\nBonjour à tous ! La réunion de demain est déplacée à 14 h.\n\n- Apportez vos notes\n- La salle change : B204",
                Done = true,
            };
        TranslateService.ShowSample(job);
        _sample = true;
        _dirty = true;
        Refresh();
    }
}
