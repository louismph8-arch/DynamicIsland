using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Vault;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Presse-papiers » de l'île : l'historique (filtre Tout / Texte / Liens / Images / Épinglés ; clic = recopier,
/// épingler, supprimer, « Garder dans le coffre » pour un texte) et le coffre (nom + identifiant ; « Copier » = mot de passe
/// exclu des historiques, « Copier l'identifiant », modifier). Le mot de passe n'est JAMAIS affiché dans l'île ; la saisie se
/// fait dans une fenêtre activable (l'île ne prend pas le clavier). Mis à jour seulement quand l'onglet est ouvert.
/// </summary>
public sealed class ClipboardView : UserControl
{
    private enum Filter { All, Text, Links, Images, Pinned }
    private const int Page = 60;

    private readonly TextBlock _historyTab, _vaultTab, _status;
    private readonly StackPanel _actions = new() { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _filters = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(Space.S2, Space.S2, 0, Space.S2) };
    private readonly ContentControl _offer = new();
    private readonly StackPanel _list = new();
    private readonly ScrollViewer _scroller;
    private readonly DispatcherTimer _statusTimer = new() { Interval = TimeSpan.FromSeconds(2.5) };
    private bool _active, _dirty = true, _vault;
    private Filter _filter;
    private int _shown = Page;

    // Rendus --snapshot : données d'exemple en mémoire (ni le vrai presse-papiers, ni le vrai coffre).
    private List<ClipItem>? _sampleItems;
    private List<VaultEntry>? _sampleEntries;
    private bool _sampleOffer;

    public ClipboardView()
    {
        Width = IslandShape.ClipboardContentWidth;
        var panel = new DockPanel { LastChildFill = true };

        var header = new Grid { Margin = new Thickness(Space.S2, 0, Space.S1, 0) };
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        header.ColumnDefinitions.Add(new ColumnDefinition());
        header.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var segments = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        _historyTab = Segment(Icons.Clipboard, Loc.T("clipboard.history"), false);
        _vaultTab = Segment(Icons.Key, Loc.T("clipboard.vault"), true);
        segments.Children.Add(_historyTab);
        segments.Children.Add(_vaultTab);
        header.Children.Add(segments);
        _status = new TextBlock { FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, Space.S2, 0), TextTrimming = TextTrimming.CharacterEllipsis };
        Grid.SetColumn(_status, 1);
        header.Children.Add(_status);
        Grid.SetColumn(_actions, 2);
        header.Children.Add(_actions);
        DockPanel.SetDock(header, System.Windows.Controls.Dock.Top);
        panel.Children.Add(header);

        DockPanel.SetDock(_filters, System.Windows.Controls.Dock.Top);
        panel.Children.Add(_filters);
        DockPanel.SetDock(_offer, System.Windows.Controls.Dock.Top);
        panel.Children.Add(_offer);

        _scroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Focusable = false,
            Content = _list,
            Height = IslandShape.ClipboardListHeight,
        };
        if (Application.Current?.TryFindResource("ThinScrollBar") is Style thin) _scroller.Resources.Add(typeof(System.Windows.Controls.Primitives.ScrollBar), thin);
        panel.Children.Add(_scroller);
        Content = panel;

        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            _status.Text = "";
        };
        ClipboardHistory.Store.Changed += OnDataChanged;
        PasswordVault.Changed += OnDataChanged;
        _vaultTab.Opacity = 0.5; // contenu construit à la première ouverture de l'onglet (rien au démarrage)
    }

    public bool IsActive
    {
        get => _active;
        set
        {
            if (_active == value) return;
            _active = value;
            if (!value) return;
            // Sans Windows Hello, le compte Windows suffit : le coffre s'ouvre tout seul quand on regarde l'onglet.
            if (_vault && _sampleEntries == null && PasswordVault.IsLocked && PasswordVault.HelloAvailable == false) _ = PasswordVault.UnlockAsync();
            if (_dirty) Refresh();
        }
    }

    /// <summary>Ouvre l'onglet sur le coffre (palette « mot de passe … », annonce).</summary>
    public void ShowVault(bool vault)
    {
        _vault = vault;
        ShowSegment();
    }

    private void OnDataChanged() => Dispatcher.BeginInvoke(() =>
    {
        _dirty = true;
        if (_active) Refresh();
    });

    // ------------------------------------------------------------------ en-tête

    private TextBlock Segment(string glyph, string text, bool vault)
    {
        var block = new TextBlock
        {
            FontSize = TypeRamp.Footnote,
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, Space.S6, 0),
            Cursor = Cursors.Hand,
            Background = Paint.HitTarget,
            VerticalAlignment = VerticalAlignment.Center,
        };
        block.Inlines.Add(new System.Windows.Documents.Run(glyph) { FontFamily = FontStack.Icons, FontSize = IconSize.S });
        block.Inlines.Add(new System.Windows.Documents.Run("  " + text));
        block.MouseLeftButtonUp += (_, e) =>
        {
            _vault = vault;
            ShowSegment();
            e.Handled = true;
        };
        return block;
    }

    private void ShowSegment()
    {
        _historyTab.Opacity = _vault ? 0.5 : 1;
        _vaultTab.Opacity = _vault ? 1 : 0.5;
        _shown = Page;
        if (_vault && _active && _sampleEntries == null && PasswordVault.IsLocked && PasswordVault.HelloAvailable == false) _ = PasswordVault.UnlockAsync();
        Refresh();
        _scroller.ScrollToTop();
    }

    private void Say(string text)
    {
        _status.Text = text;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    // ------------------------------------------------------------------ contenu

    private void Refresh()
    {
        _dirty = false;
        _actions.Children.Clear();
        _filters.Children.Clear();
        _list.Children.Clear();
        ShowOffer();
        if (_vault) ShowVaultList();
        else ShowHistory();
    }

    /// <summary>Mot de passe copié par un gestionnaire : jamais dans l'historique, mais on propose de le garder (60 s).</summary>
    private void ShowOffer()
    {
        bool offer = _sampleItems != null ? _sampleOffer : ClipboardHistory.Store.HasSecret;
        if (!offer)
        {
            _offer.Content = null;
            return;
        }
        var row = new Grid { Margin = new Thickness(Space.S3, Space.S2, Space.S3, Space.S2) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(Glyph(Icons.Key, Paint.TextPrimary));
        var text = new TextBlock { Text = Loc.T("clipboard.offer"), FontSize = TypeRamp.Footnote, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S3, 0, Space.S3, 0), TextTrimming = TextTrimming.CharacterEllipsis, ToolTip = Loc.T("clipboard.offer.detail") };
        Grid.SetColumn(text, 1);
        row.Children.Add(text);
        var keep = AccentChip(Loc.T("clipboard.keep_in_vault"));
        keep.Click += (_, _) =>
        {
            if (_sampleItems != null) return;
            if (ClipboardHistory.Store.TakeSecret() is { } secret) VaultEditorWindow.Open(null, secret);
        };
        Grid.SetColumn(keep, 2);
        row.Children.Add(keep);
        _offer.Content = new SquircleBorder { CornerRadius = Corners.L, Background = Paint.Control, Margin = new Thickness(Space.S1, 0, Space.S1, Space.S2), Child = row };
    }

    private void ShowHistory()
    {
        var settings = SettingsStore.Current.Clipboard;
        // Écrire soi-même un texte (l'île ne prend pas le clavier : la saisie se fait dans une petite fenêtre).
        var create = AccentChip(Icons.Plus, Loc.T("clipboard.new"));
        create.ToolTip = Loc.T("clipboard.new.tooltip");
        create.Margin = new Thickness(0, 0, Space.S2, 0);
        create.Click += (_, _) =>
        {
            if (_sampleItems == null) ClipTextWindow.Open();
        };
        _actions.Children.Add(create);
        var clear = IconButton(Icons.Eraser, Loc.T("clipboard.clear"));
        clear.Click += (_, _) => ClearHistory();
        _actions.Children.Add(clear);

        foreach (var filter in Enum.GetValues<Filter>())
        {
            var chip = new Button
            {
                Style = (Style)FindResource("ChipButton"),
                Content = filter switch
                {
                    Filter.Text => Loc.T("clipboard.filter.text"),
                    Filter.Links => Loc.T("clipboard.filter.links"),
                    Filter.Images => Loc.T("clipboard.filter.images"),
                    Filter.Pinned => Loc.T("clipboard.filter.pinned"),
                    _ => Loc.T("clipboard.filter.all"),
                },
                Margin = new Thickness(0, 0, Space.S2, 0),
                Background = filter == _filter ? Paint.Selected : Paint.Surface,
                Foreground = filter == _filter ? Paint.TextPrimary : Paint.TextSecondary,
            };
            chip.Click += (_, _) =>
            {
                _filter = filter;
                _shown = Page;
                Refresh();
                _scroller.ScrollToTop();
            };
            _filters.Children.Add(chip);
        }

        var items = (_sampleItems ?? ClipboardHistory.Store.Items).Where(Matches).ToList();
        if (!settings.History && _sampleItems == null) _list.Children.Add(Empty(Icons.Clipboard, Loc.T("clipboard.off")));
        else if (items.Count == 0) _list.Children.Add(Empty(Icons.Clipboard, Loc.T(_filter == Filter.All ? "clipboard.empty" : "clipboard.empty.filter")));
        // Épinglés d'abord, puis du plus récent au plus ancien.
        foreach (var item in items.OrderByDescending(i => i.Pinned).Take(_shown)) _list.Children.Add(HistoryRow(item));
        if (items.Count > _shown)
        {
            var more = new Button { Style = (Style)FindResource("ChipButton"), Content = Loc.F("clipboard.more", items.Count - _shown), HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S3, 0, Space.S3) };
            more.Click += (_, _) =>
            {
                _shown += Page;
                Refresh();
            };
            _list.Children.Add(more);
        }
    }

    private bool Matches(ClipItem item) => _filter switch
    {
        Filter.Text => item.Kind == ClipKind.Text,
        Filter.Links => item.Kind == ClipKind.Link,
        Filter.Images => item.Kind == ClipKind.Image,
        Filter.Pinned => item.Pinned,
        _ => true,
    };

    private FrameworkElement HistoryRow(ClipItem item)
    {
        var row = RowGrid();
        FrameworkElement icon = item.Kind == ClipKind.Image && item.Thumbnail is { } thumbnail
            ? new SquircleBorder
            {
                Width = IslandShape.ClipboardThumb, Height = IslandShape.ClipboardThumb, CornerRadius = Corners.S, VerticalAlignment = VerticalAlignment.Center,
                Child = new Image { Source = thumbnail, Stretch = Stretch.UniformToFill },
            }
            : Glyph(item.Kind == ClipKind.Link ? Icons.Link : item.Kind == ClipKind.Image ? Icons.Picture : Icons.TextItem, Paint.TextSecondary);
        row.Children.Add(icon);

        string title = item.Kind == ClipKind.Image ? Loc.F("clipboard.image", item.Width, item.Height) : Preview(item.Text);
        var caption = (item.Pinned ? Icons.Pin + "  " : "") + When(item.Time);
        var texts = Texts(title, caption, item.Pinned);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        if (item.Kind != ClipKind.Image)
        {
            var edit = IconButton(Icons.Pencil, Loc.T("clipboard.edit"));
            edit.Click += (_, _) =>
            {
                if (_sampleItems == null) ClipTextWindow.Open(item);
            };
            buttons.Children.Add(edit);
        }
        if (item.Kind == ClipKind.Text)
        {
            var keep = IconButton(Icons.Key, Loc.T("clipboard.keep_in_vault"));
            keep.Click += (_, _) =>
            {
                if (_sampleItems == null) VaultEditorWindow.Open(null, item.Text.Trim());
            };
            buttons.Children.Add(keep);
        }
        var pin = IconButton(item.Pinned ? Icons.PinOff : Icons.Pin, Loc.T(item.Pinned ? "clipboard.unpin" : "clipboard.pin"));
        pin.Click += (_, _) =>
        {
            if (_sampleItems == null) ClipboardHistory.Store.Pin(item.Id, !item.Pinned);
        };
        buttons.Children.Add(pin);
        var remove = IconButton(Icons.Close, Loc.T("clipboard.remove"));
        remove.Click += (_, _) =>
        {
            if (_sampleItems == null) ClipboardHistory.Store.Remove(item.Id);
        };
        buttons.Children.Add(remove);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);

        return Clickable(row, Loc.T("clipboard.copy.hint"), () =>
        {
            if (_sampleItems != null) return;
            Say(ClipboardHistory.Copy(item) ? Loc.T("clipboard.copied") : Loc.T("clipboard.copy.failed"));
        });
    }

    private void ShowVaultList()
    {
        bool sample = _sampleEntries != null;
        if (!sample && PasswordVault.IsBroken)
        {
            _list.Children.Add(Empty(Icons.Lock, Loc.T("vault.error.broken")));
            return;
        }
        if (!sample && PasswordVault.IsLocked)
        {
            var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S8, 0, 0) };
            panel.Children.Add(new TextBlock { Text = Icons.Lock, FontFamily = FontStack.Icons, FontSize = IconSize.XL, Foreground = Paint.TextSecondary, HorizontalAlignment = HorizontalAlignment.Center });
            panel.Children.Add(new TextBlock { Text = Loc.T("vault.locked"), FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, Margin = new Thickness(0, Space.S3, 0, Space.S4), HorizontalAlignment = HorizontalAlignment.Center });
            var unlock = AccentChip(Loc.T(PasswordVault.HelloAvailable == true ? "vault.unlock.hello" : "vault.unlock"));
            unlock.HorizontalAlignment = HorizontalAlignment.Center;
            unlock.Click += async (_, _) =>
            {
                if (!await PasswordVault.UnlockAsync()) Say(Loc.T("vault.unlock.failed"));
            };
            panel.Children.Add(unlock);
            _list.Children.Add(panel);
            return;
        }

        var add = IconButton(Icons.Plus, Loc.T("vault.add"));
        add.Click += (_, _) =>
        {
            if (!sample) VaultEditorWindow.Open(null);
        };
        _actions.Children.Add(add);
        var lockNow = IconButton(Icons.Lock, Loc.T("vault.lock"));
        lockNow.Click += (_, _) =>
        {
            if (!sample) PasswordVault.Lock();
        };
        _actions.Children.Add(lockNow);

        var entries = _sampleEntries ?? PasswordVault.Entries.ToList();
        if (entries.Count == 0) _list.Children.Add(Empty(Icons.Key, Loc.T("vault.empty")));
        foreach (var entry in entries) _list.Children.Add(VaultRow(entry, sample));
    }

    private FrameworkElement VaultRow(VaultEntry entry, bool sample)
    {
        var row = RowGrid();
        row.Children.Add(Glyph(Icons.Key, Paint.TextSecondary));
        var texts = Texts(entry.Name, entry.Login.Length > 0 ? entry.Login : Loc.T("vault.no_login"), false);
        Grid.SetColumn(texts, 1);
        row.Children.Add(texts);

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        var copy = AccentChip(Icons.Copy, Loc.T("vault.copy"));
        copy.ToolTip = Loc.T("vault.copy.tooltip");
        copy.Margin = new Thickness(0, 0, Space.S2, 0);
        copy.Click += (_, _) =>
        {
            if (sample) return;
            Say(PasswordVault.CopyPassword(entry)
                ? Loc.T(SettingsStore.Current.Clipboard.AutoClear ? "vault.copied.cleared" : "vault.copied")
                : Loc.T("clipboard.copy.failed"));
        };
        buttons.Children.Add(copy);
        var login = IconButton(Icons.User, Loc.T("vault.copy_login"));
        login.IsEnabled = entry.Login.Length > 0;
        login.Click += (_, _) =>
        {
            if (!sample) Say(PasswordVault.CopyLogin(entry) ? Loc.T("vault.login_copied") : Loc.T("clipboard.copy.failed"));
        };
        buttons.Children.Add(login);
        var edit = IconButton(Icons.Pencil, Loc.T("vault.edit"));
        edit.Click += (_, _) =>
        {
            if (!sample) VaultEditorWindow.Open(entry);
        };
        buttons.Children.Add(edit);
        Grid.SetColumn(buttons, 2);
        row.Children.Add(buttons);
        return new SquircleBorder { CornerRadius = Corners.M, Child = row, Background = Paint.HitTarget };
    }

    private void ClearHistory()
    {
        if (_sampleItems != null) return;
        const MessageBoxOptions onTop = MessageBoxOptions.DefaultDesktopOnly; // l'île ne prend jamais le focus
        if (MessageBox.Show(Loc.T("clipboard.clear.confirm"), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Question,
                MessageBoxResult.Cancel, onTop) != MessageBoxResult.OK) return;
        ClipboardHistory.Store.Clear();
    }

    // ------------------------------------------------------------------ petits éléments

    private static Grid RowGrid()
    {
        var row = new Grid { Height = IslandShape.ClipboardRow, Margin = new Thickness(Space.S2, 0, Space.S1, 0) };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(IslandShape.ClipboardThumb) });
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        return row;
    }

    private static StackPanel Texts(string title, string caption, bool strong)
    {
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(Space.S4, 0, Space.S3, 0) };
        texts.Children.Add(new TextBlock { Text = title, FontSize = TypeRamp.Footnote, FontWeight = strong ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis });
        var small = new TextBlock { FontSize = TypeRamp.Caption, Foreground = Paint.TextSecondary, TextTrimming = TextTrimming.CharacterEllipsis };
        // Le glyphe d'épingle est dans la police d'icônes, le reste dans celle de l'interface.
        if (caption.StartsWith(Icons.Pin, StringComparison.Ordinal))
        {
            small.Inlines.Add(new System.Windows.Documents.Run(Icons.Pin) { FontFamily = FontStack.Icons });
            caption = caption[Icons.Pin.Length..];
        }
        small.Inlines.Add(new System.Windows.Documents.Run(caption));
        texts.Children.Add(small);
        return texts;
    }

    /// <summary>Ligne cliquable (recopier) : léger fond au survol ; un clic sur un bouton de la ligne ne compte pas.</summary>
    private static SquircleBorder Clickable(Grid row, string tooltip, Action click)
    {
        var border = new SquircleBorder { CornerRadius = Corners.M, Child = row, Background = Paint.HitTarget, Cursor = Cursors.Hand, ToolTip = tooltip };
        border.MouseEnter += (_, _) => border.Background = Paint.Card;
        border.MouseLeave += (_, _) => border.Background = Paint.HitTarget;
        border.MouseLeftButtonUp += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject source && FindButton(source, border)) return;
            click();
            e.Handled = true;
        };
        return border;
    }

    private static bool FindButton(DependencyObject source, DependencyObject stop)
    {
        for (var node = source; node != null && node != stop; node = VisualTreeHelper.GetParent(node) ?? LogicalTreeHelper.GetParent(node))
            if (node is Button) return true;
        return false;
    }

    private static TextBlock Glyph(string glyph, Brush brush) => new()
    {
        Text = glyph,
        FontFamily = FontStack.Icons,
        FontSize = IconSize.M,
        Foreground = brush,
        HorizontalAlignment = HorizontalAlignment.Center,
        VerticalAlignment = VerticalAlignment.Center,
    };

    private Button IconButton(string glyph, string tooltip) => new()
    {
        Style = (Style)FindResource("SmallIconButton"),
        Content = glyph,
        ToolTip = tooltip,
        Margin = new Thickness(Space.S1, 0, 0, 0),
    };

    private Button AccentChip(string text) => AccentChip(null, text);

    /// <summary>Bouton bien visible (dégradé d'accent) : « Copier », « Garder dans le coffre », « Déverrouiller ».</summary>
    private Button AccentChip(string? glyph, string text)
    {
        var content = new TextBlock { FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, Foreground = Paint.White, VerticalAlignment = VerticalAlignment.Center };
        if (glyph != null) content.Inlines.Add(new System.Windows.Documents.Run(glyph + "  ") { FontFamily = FontStack.Icons });
        content.Inlines.Add(new System.Windows.Documents.Run(text));
        var button = new Button { Style = (Style)FindResource("ChipButton"), Content = content, VerticalAlignment = VerticalAlignment.Center };
        button.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        return button;
    }

    private static FrameworkElement Empty(string glyph, string text)
    {
        var panel = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(Space.S8, Space.S10, Space.S8, 0) };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.XL, Foreground = Paint.TextTertiary, HorizontalAlignment = HorizontalAlignment.Center });
        panel.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, Space.S3, 0, 0) });
        return panel;
    }

    private static string Preview(string text)
    {
        var line = string.Join(" ", text.Split((char[])['\r', '\n', '\t'], StringSplitOptions.RemoveEmptyEntries)).Trim();
        return line.Length > 160 ? line[..160] + "…" : line;
    }

    private static string When(DateTime time) =>
        time.Date == DateTime.Today ? time.ToString("t", Loc.Culture)
        : time.Date == DateTime.Today.AddDays(-1) ? Loc.F("clipboard.yesterday", time.ToString("t", Loc.Culture))
        : time.ToString("d MMM", Loc.Culture);

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : historique et coffre d'exemple (rien de réel n'est lu).</summary>
    internal void ShowSample(bool vault)
    {
        var now = DateTime.Now;
        _sampleItems =
        [
            new ClipItem { Kind = ClipKind.Text, Text = Loc.T("clipboard.sample.text1"), Time = now.AddMinutes(-2) },
            new ClipItem { Kind = ClipKind.Link, Text = "https://github.com/louismph8-arch/DynamicIsland", Time = now.AddMinutes(-9) },
            new ClipItem { Kind = ClipKind.Text, Text = Loc.T("clipboard.sample.text2"), Time = now.AddHours(-3), Pinned = true },
            new ClipItem { Kind = ClipKind.Text, Text = "git push origin main", Time = now.AddDays(-1) },
        ];
        _sampleEntries =
        [
            new VaultEntry { Name = Loc.T("vault.sample.site"), Login = Loc.T("vault.sample.login") },
            new VaultEntry { Name = Loc.T("vault.sample.game"), Login = "Pseudo_Opaline" },
            new VaultEntry { Name = Loc.T("vault.sample.wifi"), Login = "" },
        ];
        _sampleOffer = !vault;
        _filter = Filter.All;
        _vault = vault;
        ShowSegment();
    }
}
