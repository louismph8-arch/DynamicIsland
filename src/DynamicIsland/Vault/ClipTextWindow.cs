using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Vault;

/// <summary>
/// « + Nouveau » de l'onglet Presse-papiers (et « Modifier » d'un texte de l'historique) : petite fenêtre activable (l'île ne
/// prend jamais le clavier) pour écrire un texte sur plusieurs lignes. « Ajouter » le range en tête de l'historique (épinglé si
/// on veut) et, case cochée par défaut, le copie dans le presse-papiers — par Opaline, donc pas enregistré une 2e fois.
/// </summary>
public sealed class ClipTextWindow : CardWindow
{
    private readonly ClipItem? _item;
    private readonly TextBox _text = new();
    private readonly CheckBox _pin = new(), _copy = new();
    private readonly TextBlock _status = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
    private static ClipTextWindow? _open;

    /// <summary>Ouvre la fenêtre : <paramref name="item"/> null = nouveau texte (pré-rempli avec <paramref name="text"/>).</summary>
    public static void Open(ClipItem? item = null, string? text = null)
    {
        try
        {
            _open?.Close();
            var window = new ClipTextWindow(item, text, snapshot: false);
            _open = window;
            window.Closed += (_, _) =>
            {
                if (_open == window) _open = null;
            };
            window.Topmost = true; // l'île (au premier plan) ne doit pas la cacher
            window.Show();
            window.Activate();
        }
        catch (Exception ex)
        {
            if (!ModuleGuard.TryFail(ex)) Log.Error("Presse-papiers : fenêtre « Nouveau texte »", ex);
        }
    }

    private ClipTextWindow(ClipItem? item, string? text, bool snapshot)
        : base(Loc.T(item == null ? "clipboard.new.title" : "clipboard.edit.title"), CardShape.ClipWidth, CardShape.ClipHeight, snapshot)
    {
        _item = item;
        Body.Children.Add(BuildBody(item?.Text ?? text ?? "", item?.Pinned ?? false));
        BuildFooter();
        Loaded += (_, _) =>
        {
            _text.Focus();
            _text.CaretIndex = _text.Text.Length;
        };
    }

    private FrameworkElement BuildBody(string text, bool pinned)
    {
        var page = new StackPanel { Margin = new Thickness(Space.S14, Space.S10, Space.S14, Space.S4) };
        var title = new StackPanel { Orientation = Orientation.Horizontal };
        title.Children.Add(Badge(Icons.ClipboardPlus));
        var heading = Heading(Loc.T(_item == null ? "clipboard.new.title" : "clipboard.edit.title"));
        heading.Margin = new Thickness(Space.S6, 0, 0, 0);
        heading.VerticalAlignment = VerticalAlignment.Center;
        title.Children.Add(heading);
        page.Children.Add(title);
        page.Children.Add(Paragraph(Loc.T("clipboard.new.description")));

        _text.Style = (Style)FindResource("HexBox");
        _text.Width = double.NaN;
        _text.FontFamily = FontStack.Ui;
        _text.MaxLength = ClipboardHistoryStore.MaxTextLength;
        _text.Text = text;
        _text.Height = CardShape.ClipTextHeight;
        _text.AcceptsReturn = true;
        _text.AcceptsTab = true;
        _text.TextWrapping = TextWrapping.Wrap;
        _text.VerticalContentAlignment = VerticalAlignment.Top;
        _text.VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _text.Padding = new Thickness(0, Space.S2, 0, Space.S2);
        _text.Margin = new Thickness(0, Space.S6, 0, Space.S4);
        page.Children.Add(_text);

        page.Children.Add(Option(_pin, Loc.T("clipboard.new.pin"), pinned));
        page.Children.Add(Option(_copy, Loc.T("clipboard.new.copy"), _item == null));

        int index = 0;
        foreach (UIElement child in page.Children) Reveal(child, index++);
        return new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = page };
    }

    private Grid Option(CheckBox box, string text, bool on)
    {
        var row = new Grid { Margin = new Thickness(0, Space.S2, 0, Space.S2) };
        row.ColumnDefinitions.Add(new ColumnDefinition());
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Body, VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap });
        box.Style = (Style)FindResource("SwitchToggle");
        box.IsChecked = on;
        box.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(box, 1);
        row.Children.Add(box);
        return row;
    }

    private void BuildFooter()
    {
        Footer.ColumnDefinitions.Add(new ColumnDefinition());
        Footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        _status.Margin = new Thickness(0, 0, Space.S6, 0);
        _status.Text = Loc.T("clipboard.new.hint");
        Footer.Children.Add(_status);
        var buttons = new StackPanel { Orientation = Orientation.Horizontal };
        var cancel = SecondaryButton(Loc.T("vault.editor.cancel"));
        cancel.Margin = new Thickness(0, 0, Space.S4, 0);
        cancel.Click += (_, _) => CloseAnimated();
        var add = PrimaryButton(Loc.T(_item == null ? "clipboard.new.add" : "vault.editor.save"));
        add.Click += (_, _) => Commit();
        buttons.Children.Add(cancel);
        buttons.Children.Add(add);
        Grid.SetColumn(buttons, 1);
        Footer.Children.Add(buttons);
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                Commit();
                e.Handled = true;
            }
        };
    }

    private void Commit()
    {
        var text = _text.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            _status.Text = Loc.T("clipboard.new.empty");
            _text.Focus();
            return;
        }
        if (Snapshot) return;
        var item = _item == null
            ? ClipboardHistory.Store.AddManual(text, _pin.IsChecked == true)
            : ClipboardHistory.Store.Edit(_item.Id, text, _pin.IsChecked == true);
        if (item == null)
        {
            _status.Text = Loc.T("clipboard.copy.failed");
            return;
        }
        if (_copy.IsChecked == true && !ClipboardHistory.Copy(item))
        {
            _status.Text = Loc.T("clipboard.copy.failed"); // gardé dans l'historique, mais le presse-papiers est occupé
            return;
        }
        CloseAnimated();
    }

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

    /// <summary>Rendu --snapshot : un texte d'exemple (rien n'est ajouté à l'historique ni copié).</summary>
    internal static async Task RenderSnapshotAsync(string folder)
    {
        var window = new ClipTextWindow(null, Loc.T("clipboard.sample.text2"), snapshot: true);
        window.Show();
        await Task.Delay(300);
        await SnapshotRenderer.SaveAsync(window, Path.Combine(folder, "3m-presse-papiers-nouveau.png"));
        window.Close();
    }
}
