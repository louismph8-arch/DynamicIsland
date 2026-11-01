using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Vault;

/// <summary>
/// Fenêtre d'une entrée du coffre (activable : l'île ne prend jamais le clavier) : nom du site ou de l'appli, identifiant,
/// mot de passe (caché, œil pour l'afficher), note. « Enregistrer » range l'entrée chiffrée ; « Annuler » ne garde rien —
/// même un mot de passe proposé depuis le presse-papiers est alors oublié.
/// </summary>
public sealed class VaultEditorWindow : CardWindow
{
    private readonly VaultEntry _entry;
    private readonly bool _isNew;
    private readonly TextBox _name = new(), _login = new(), _shown = new(), _note = new();
    private readonly PasswordBox _password = new();
    private readonly Button _reveal = new();
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static VaultEditorWindow? _open;

    /// <summary>
    /// Ouvre l'éditeur (le coffre est déverrouillé d'abord, par Windows Hello s'il est configuré).
    /// <paramref name="entry"/> null = nouvelle entrée ; <paramref name="password"/> = mot de passe pré-rempli (copié).
    /// </summary>
    public static async void Open(VaultEntry? entry, string? password = null)
    {
        try
        {
            if (!await PasswordVault.UnlockAsync()) return;
            _open?.Close();
            var editor = new VaultEditorWindow(entry, password, snapshot: false);
            _open = editor;
            editor.Closed += (_, _) =>
            {
                if (_open == editor) _open = null;
            };
            editor.Topmost = true; // l'île (au premier plan) ne doit pas la cacher
            editor.Show();
            editor.Activate();
        }
        catch (Exception ex)
        {
            if (!ModuleGuard.TryFail(ex)) Log.Error("Coffre : ouverture de l'éditeur", ex);
        }
    }

    private VaultEditorWindow(VaultEntry? entry, string? password, bool snapshot)
        : base(Loc.T(entry == null ? "vault.editor.new" : "vault.editor.edit"), CardShape.VaultWidth, CardShape.VaultHeight, snapshot)
    {
        _isNew = entry == null;
        _entry = entry?.Clone() ?? new VaultEntry();
        if (password != null) _entry.Password = password;
        Body.Children.Add(BuildBody());
        BuildFooter();
        Loaded += (_, _) => _name.Focus();
    }

    private FrameworkElement BuildBody()
    {
        var page = new StackPanel { Margin = new Thickness(Space.S14, Space.S10, Space.S14, Space.S4) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Badge(Icons.Key));
        var heading = Heading(Loc.T(_isNew ? "vault.editor.new" : "vault.editor.edit"));
        heading.Margin = new Thickness(Space.S6, 0, 0, 0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(heading);
        page.Children.Add(title);
        page.Children.Add(Paragraph(Loc.T("vault.editor.description")));

        page.Children.Add(Label(Loc.T("vault.editor.name")));
        page.Children.Add(Field(_name, _entry.Name, 80));
        page.Children.Add(Label(Loc.T("vault.editor.login")));
        page.Children.Add(Field(_login, _entry.Login, 200));

        page.Children.Add(Label(Loc.T("vault.editor.password")));
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _password.Style = (Style)FindResource("KeyBox");
        _password.MaxLength = 256;
        _password.Password = _entry.Password;
        Field(_shown, _entry.Password, 256);
        _shown.FontFamily = FontStack.Mono;
        _shown.Visibility = Visibility.Collapsed;
        _password.PasswordChanged += (_, _) =>
        {
            if (_shown.Visibility != Visibility.Visible) _shown.Text = _password.Password;
        };
        _shown.TextChanged += (_, _) =>
        {
            if (_shown.Visibility == Visibility.Visible) _password.Password = _shown.Text;
        };
        row.Children.Add(_password);
        row.Children.Add(_shown);
        _reveal.Style = (Style)FindResource("SettingsButton");
        _reveal.Content = new TextBlock { Text = Icons.Eye, FontFamily = FontStack.Icons, FontSize = IconSize.M };
        _reveal.ToolTip = Loc.T("vault.editor.show");
        _reveal.Margin = new Thickness(Space.S4, 0, 0, 0);
        _reveal.Click += (_, _) => ToggleReveal();
        Grid.SetColumn(_reveal, 1);
        row.Children.Add(_reveal);
        page.Children.Add(row);

        page.Children.Add(Label(Loc.T("vault.editor.note")));
        Field(_note, _entry.Note, 1000);
        _note.Height = CardShape.NoteHeight;
        _note.AcceptsReturn = true;
        _note.TextWrapping = TextWrapping.Wrap;
        _note.VerticalContentAlignment = VerticalAlignment.Top;
        _note.Padding = new Thickness(0, Space.S2, 0, Space.S2);
        page.Children.Add(_note);

        var safety = Paragraph(Loc.T("vault.editor.safety"));
        safety.FontSize = TypeRamp.Footnote;
        safety.Margin = new Thickness(0, Space.S6, 0, 0);
        page.Children.Add(safety);

        int index = 0;
        foreach (UIElement child in page.Children) Reveal(child, index++);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private void BuildFooter()
    {
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        if (!_isNew)
        {
            var delete = SecondaryButton(Loc.T("vault.editor.delete"));
            delete.Foreground = Paint.Danger;
            delete.Margin = new Thickness(0, 0, Space.S6, 0);
            delete.Click += (_, _) => Delete();
            Footer.Children.Add(delete);
        }
        _status.Margin = new Thickness(0, 0, Space.S6, 0);
        Grid.SetColumn(_status, 1);
        Footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = SecondaryButton(Loc.T("vault.editor.cancel"));
        cancel.Margin = new Thickness(0, 0, Space.S4, 0);
        cancel.Click += (_, _) => CloseAnimated();
        var save = PrimaryButton(Loc.T("vault.editor.save"));
        save.Click += (_, _) => Save();
        buttons.Children.Add(cancel);
        buttons.Children.Add(save);
        Grid.SetColumn(buttons, 2);
        Footer.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.FocusedElement != _note && Keyboard.FocusedElement is TextBox or PasswordBox)
            {
                Save();
                e.Handled = true;
            }
        };
    }

    private void ToggleReveal()
    {
        bool show = _shown.Visibility != Visibility.Visible;
        if (show) _shown.Text = _password.Password;
        _shown.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        _password.Visibility = show ? Visibility.Collapsed : Visibility.Visible;
        ((TextBlock)_reveal.Content).Text = show ? Icons.EyeOff : Icons.Eye;
        _reveal.ToolTip = Loc.T(show ? "vault.editor.hide" : "vault.editor.show");
    }

    private async void Save()
    {
        _entry.Name = _name.Text.Trim();
        _entry.Login = _login.Text.Trim();
        _entry.Password = _password.Password;
        _entry.Note = _note.Text.Trim();
        if (_entry.Name.Length == 0)
        {
            _status.Text = Loc.T("vault.editor.missing_name");
            _name.Focus();
            return;
        }
        if (_entry.Password.Length == 0)
        {
            _status.Text = Loc.T("vault.editor.missing_password");
            _password.Focus();
            return;
        }
        try
        {
            // Reverrouillé pendant qu'on écrivait (5 min) : on redemande Windows Hello avant d'écrire.
            if (PasswordVault.IsLocked && !await PasswordVault.UnlockAsync()) return;
            if (!PasswordVault.Save(_entry))
            {
                _status.Text = Loc.T(PasswordVault.IsBroken ? "vault.error.broken" : "vault.error.save");
                return;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Log.Error("Coffre : enregistrement", ex);
            _status.Text = Loc.T("vault.error.save");
            return;
        }
        CloseAnimated();
    }

    private void Delete()
    {
        if (MessageBox.Show(this, Loc.F("vault.delete.confirm", _entry.Name), AppInfo.Name, MessageBoxButton.OKCancel, MessageBoxImage.Warning,
                MessageBoxResult.Cancel) != MessageBoxResult.OK) return;
        try
        {
            if (PasswordVault.Delete(_entry.Id)) CloseAnimated();
            else _status.Text = Loc.T("vault.error.save");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Security.Cryptography.CryptographicException)
        {
            Log.Error("Coffre : suppression", ex);
            _status.Text = Loc.T("vault.error.save");
        }
    }

    // ------------------------------------------------------------------ petits éléments

    private TextBox Field(TextBox box, string text, int max)
    {
        box.Style = (Style)FindResource("HexBox");
        box.Width = double.NaN;
        box.MaxLength = max;
        box.FontFamily = FontStack.Ui;
        box.Text = text;
        return box;
    }

    private TextBlock Label(string text) => new()
    {
        Text = text,
        Style = (Style)FindResource("CardTitle"),
        Margin = new Thickness(0, Space.S5, 0, Space.S3),
    };

    private static SquircleBorder Badge(string glyph)
    {
        var badge = new SquircleBorder
        {
            Width = WelcomeShape.TipIcon, Height = WelcomeShape.TipIcon, CornerRadius = Corners.Pill, VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.L, Foreground = Paint.White,
                                    HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        badge.SetResourceReference(Border.BackgroundProperty, AppearanceService.AccentGradientKey);
        return badge;
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Rendu --snapshot : une entrée d'exemple (rien n'est lu ni écrit dans le vrai coffre).</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var sample = new VaultEntry { Name = Loc.T("vault.sample.site"), Login = Loc.T("vault.sample.login"), Password = "exemple-123", Note = Loc.T("vault.sample.note") };
        var window = new VaultEditorWindow(sample, null, snapshot: true);
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "17-coffre-entree.png"));
        window.Close();
    }
}
