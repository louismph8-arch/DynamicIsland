using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;
using DynamicIsland.Controls;
using DynamicIsland.Services;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Views;

/// <summary>
/// Onglet « Claude » de l'île (5J) : la conversation (questions à droite, réponses qui s'écrivent au fil de l'eau) et,
/// en bas, « Poser une question » — la barre de saisie sort de l'île, qui ne prend jamais le clavier —, « Arrêter » et
/// « Nouvelle conversation ». Voir <see cref="ClaudeService"/>.
/// </summary>
public sealed partial class ClaudeView : UserControl
{
    private readonly StackPanel _messages = new();
    private readonly ScrollViewer _scroller;
    private readonly StackPanel _empty = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly TextBlock _emptyHint = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextSecondary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center };
    private readonly TextBlock _cliText = new() { FontSize = TypeRamp.Footnote, Foreground = Paint.TextTertiary, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, Space.S3, 0, 0) };
    private readonly Button _loginButton;
    private readonly TextBlock _modelText = new() { FontSize = TypeRamp.Caption, Foreground = Paint.TextTertiary, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _ask, _stop, _newChat;
    private readonly DispatcherTimer _dots = new() { Interval = TimeSpan.FromMilliseconds(400) };
    private int _dotCount;
    private bool _active;
    private bool _followBottom = true;
    private IReadOnlyList<ClaudeTurn>? _sample;
    private ClaudeTurn? _shownLast;
    private int _shownCount = -1;
    private TextBlock? _liveAnswer;

    /// <summary>« Poser une question » : la barre de saisie (palette) doit s'ouvrir depuis l'île.</summary>
    public event Action? AskRequested;

    public ClaudeView()
    {
        Width = IslandShape.ClaudeContentWidth;
        Height = IslandShape.ClaudeContentHeight;

        _scroller = new ScrollViewer
        {
            Content = _messages,
            VerticalScrollBarVisibility = ScrollBarVisibility.Hidden, // la molette fait défiler ; pas de barre dans l'île
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };
        _scroller.ScrollChanged += (_, e) =>
        {
            // Défilement par l'utilisateur : on ne recolle au bas que s'il y retourne.
            if (e.ExtentHeightChange == 0) _followBottom = _scroller.VerticalOffset >= _scroller.ScrollableHeight - 4;
        };

        _loginButton = TextButton(Loc.T("settings.connecter"), Icons.LogIn);
        _loginButton.Click += (_, _) => ClaudeService.OpenLogin();
        _loginButton.Visibility = Visibility.Collapsed;
        _loginButton.HorizontalAlignment = HorizontalAlignment.Center;
        _loginButton.Margin = new Thickness(0, Space.S3, 0, 0);
        _empty.Children.Add(new TextBlock
        {
            Text = Icons.Sparkles, FontFamily = FontStack.Icons, FontSize = IconSize.Huge, HorizontalAlignment = HorizontalAlignment.Center,
        });
        ((TextBlock)_empty.Children[0]).SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentGradientKey);
        _empty.Children.Add(new TextBlock { Text = Loc.T("claude.view.empty"), FontSize = TypeRamp.Headline, FontWeight = FontWeights.SemiBold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, Space.S4, 0, Space.S2) });
        _empty.Children.Add(_emptyHint);
        _empty.Children.Add(_cliText);
        _empty.Children.Add(_loginButton);

        _ask = new Button { Style = (Style)FindResource("RoundButton"), Height = Sizes.Chip + Space.S4, Padding = new Thickness(Space.S6, 0, Space.S6, 0), Foreground = Paint.White };
        _ask.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        _ask.Content = Label(Icons.Sparkles, Loc.T("claude.view.ask"));
        _ask.Click += (_, _) => AskRequested?.Invoke();
        _stop = TextButton(Loc.T("claude.view.stop"), Icons.Stop);
        _stop.Click += (_, _) => ClaudeService.Stop();
        _newChat = new Button { Style = (Style)FindResource("SmallIconButton"), Content = Icons.NewChat, ToolTip = Loc.T("claude.view.new"), Margin = new Thickness(0, 0, Space.S3, 0) };
        _newChat.Click += (_, _) => ClaudeService.Clear();

        var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
        _stop.Margin = new Thickness(0, 0, Space.S3, 0);
        actions.Children.Add(_stop);
        actions.Children.Add(_newChat);
        actions.Children.Add(_ask);
        var bar = new Grid { Margin = new Thickness(0, Space.S4, 0, 0) };
        bar.ColumnDefinitions.Add(new ColumnDefinition());
        bar.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        Grid.SetColumn(actions, 1);
        bar.Children.Add(_modelText);
        bar.Children.Add(actions);

        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(bar, 1);
        layout.Children.Add(_scroller);
        layout.Children.Add(_empty);
        layout.Children.Add(bar);
        Content = layout;

        _dots.Tick += (_, _) =>
        {
            _dotCount = (_dotCount + 1) % 4;
            if (_liveAnswer is { } text && Last is { Done: false, Answer.Length: 0 }) text.Text = Loc.T("claude.view.thinking") + new string('.', _dotCount);
        };
        ClaudeService.Changed += () =>
        {
            if (_sample == null) Refresh();
        };
        SettingsStore.Changed += ShowModel;
        Refresh();
    }

    /// <summary>Vrai quand l'onglet est affiché : on vérifie alors Claude Code (installé, connecté).</summary>
    public bool IsActive
    {
        get => _active;
        set
        {
            _active = value;
            if (!value) return;
            Refresh();
            if (_sample == null && Turns.Count == 0) _ = CheckCliAsync();
        }
    }

    private IReadOnlyList<ClaudeTurn> Turns => _sample ?? ClaudeService.Turns;
    private ClaudeTurn? Last => Turns.Count > 0 ? Turns[^1] : null;

    private async Task CheckCliAsync()
    {
        _cliText.Text = Loc.T("claude.view.checking");
        var status = await ClaudeService.CheckAsync();
        _cliText.Text = status.LoggedIn ? status.Description + " " + Loc.T("claude.view.plan_note") : status.Description;
        _loginButton.Visibility = status.Installed && !status.LoggedIn ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>Redessine la conversation (entièrement si elle a changé, sinon seulement la réponse en cours).</summary>
    private void Refresh()
    {
        var turns = Turns;
        bool busy = _sample != null ? Last is { Done: false } : ClaudeService.IsBusy;
        _empty.Visibility = turns.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        _scroller.Visibility = turns.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        _stop.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
        _newChat.Visibility = turns.Count > 0 && !busy ? Visibility.Visible : Visibility.Collapsed;
        _emptyHint.Text = Loc.F("claude.view.hint", Hotkeys.Display(SettingsStore.Current.Palette.Hotkey));
        ShowModel();

        if (turns.Count != _shownCount || !ReferenceEquals(Last, _shownLast))
        {
            _messages.Children.Clear();
            _liveAnswer = null;
            foreach (var turn in turns) AddTurn(turn, turn == Last);
            _shownCount = turns.Count;
            _shownLast = Last;
            _followBottom = true;
        }
        else if (Last is { } last)
        {
            // Même conversation : seule la dernière réponse avance (ou se termine).
            _messages.Children.RemoveAt(_messages.Children.Count - 1);
            _messages.Children.Add(AnswerView(last, live: true));
        }

        if (busy && Last is { Answer.Length: 0 }) _dots.Start();
        else _dots.Stop();
        if (_followBottom) _scroller.Dispatcher.BeginInvoke(_scroller.ScrollToEnd, DispatcherPriority.Loaded);
    }

    private void ShowModel()
    {
        var model = ClaudeService.ModelName(SettingsStore.Current.Claude.Model);
        _modelText.Text = Loc.F("claude.view.model", model);
    }

    private void AddTurn(ClaudeTurn turn, bool last)
    {
        _messages.Children.Add(new SquircleBorder
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            MaxWidth = IslandShape.ClaudeBubble,
            CornerRadius = Corners.L,
            Background = Paint.Surface,
            Padding = new Thickness(Space.S5, Space.S3, Space.S5, Space.S3),
            Margin = new Thickness(0, Space.S2, 0, Space.S3),
            Child = new TextBlock { Text = turn.Question, FontSize = TypeRamp.Body, TextWrapping = TextWrapping.Wrap },
        });
        _messages.Children.Add(AnswerView(turn, last));
    }

    /// <summary>Réponse : étincelle d'accent + texte mis en forme (gras, code, listes) ; erreur en rouge ; « Copier ».</summary>
    private FrameworkElement AnswerView(ClaudeTurn turn, bool live)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, Space.S4) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        var spark = new TextBlock { Text = Icons.Sparkles, FontFamily = FontStack.Icons, FontSize = IconSize.M, Margin = new Thickness(0, Space.Px * 2, Space.S4, 0) };
        spark.SetResourceReference(TextBlock.ForegroundProperty, AppearanceService.AccentGradientKey);
        grid.Children.Add(spark);

        var column = new StackPanel();
        Grid.SetColumn(column, 1);
        grid.Children.Add(column);
        var text = new TextBlock { FontSize = TypeRamp.Body, TextWrapping = TextWrapping.Wrap, LineHeight = TypeRamp.Body * 1.45 };
        if (turn.Answer.Length > 0) Markdown(turn.Answer, text.Inlines);
        else if (!turn.Done)
        {
            text.Text = Loc.T("claude.view.thinking") + new string('.', _dotCount);
            text.Foreground = Paint.TextSecondary;
        }
        if (turn.Answer.Length > 0 || !turn.Done) column.Children.Add(text);
        if (live) _liveAnswer = text;

        if (turn.Error is { } error)
        {
            column.Children.Add(new TextBlock { Text = error, FontSize = TypeRamp.Footnote, Foreground = Paint.Danger, TextWrapping = TextWrapping.Wrap });
            if (turn.Fix != ClaudeFix.None)
            {
                var fix = turn.Fix switch
                {
                    ClaudeFix.Login => TextButton(Loc.T("settings.connecter"), Icons.LogIn),
                    ClaudeFix.ApiKey => TextButton(Loc.T("claude.view.api_key"), Icons.Settings),
                    _ => TextButton(Loc.T("claude.view.update"), Icons.Download),
                };
                fix.HorizontalAlignment = HorizontalAlignment.Left;
                fix.Margin = new Thickness(0, Space.S3, 0, 0);
                fix.Click += (_, _) =>
                {
                    if (turn.Fix == ClaudeFix.Login) ClaudeService.OpenLogin();
                    else if (turn.Fix == ClaudeFix.ApiKey) Settings.SettingsWindow.Open(Settings.SettingsPage.Island);
                    else ClaudeService.OpenUpdate();
                };
                column.Children.Add(fix);
            }
        }
        else if (turn.Done && turn.Answer.Length > 0)
        {
            var copy = TextButton(Loc.T("claude.view.copy"), Icons.Copy);
            copy.HorizontalAlignment = HorizontalAlignment.Left;
            copy.Margin = new Thickness(-Space.S3, Space.S1, 0, 0);
            copy.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(turn.Answer);
                    copy.Content = Label(Icons.Check, Loc.T("claude.view.copied"));
                }
                catch (System.Runtime.InteropServices.ExternalException)
                {
                    // Presse-papiers occupé par une autre appli : tant pis.
                }
            };
            column.Children.Add(copy);
        }
        return grid;
    }

    // ------------------------------------------------------------------ mise en forme

    [GeneratedRegex(@"\*\*(.+?)\*\*|`([^`]+)`")]
    private static partial Regex InlineMarks();

    /// <summary>Markdown léger : **gras**, `code`, titres → gras, puces « - » → « • », blocs ``` en police à chasse fixe.</summary>
    private static void Markdown(string source, InlineCollection inlines)
    {
        bool code = false;
        var lines = source.Replace("\r", "").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (line.TrimStart().StartsWith("```"))
            {
                code = !code;
                continue;
            }
            if (inlines.Count > 0) inlines.Add(new LineBreak());
            if (code)
            {
                inlines.Add(new Run(line) { FontFamily = FontStack.Mono, Foreground = Paint.TextSecondary });
                continue;
            }
            var trimmed = line.TrimStart();
            if (trimmed.StartsWith('#'))
            {
                inlines.Add(new Bold(new Run(trimmed.TrimStart('#').Trim())));
                continue;
            }
            if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
            {
                inlines.Add(new Run("•  "));
                line = trimmed[2..];
            }
            int at = 0;
            foreach (Match match in InlineMarks().Matches(line))
            {
                if (match.Index > at) inlines.Add(new Run(line[at..match.Index]));
                inlines.Add(match.Groups[1].Success
                    ? new Bold(new Run(match.Groups[1].Value))
                    : new Run(match.Groups[2].Value) { FontFamily = FontStack.Mono, Background = Paint.Surface });
                at = match.Index + match.Length;
            }
            if (at < line.Length) inlines.Add(new Run(line[at..]));
        }
    }

    // ------------------------------------------------------------------ petits éléments

    private Button TextButton(string text, string glyph) => new()
    {
        Style = (Style)FindResource("ChipButton"),
        Content = Label(glyph, text),
        Foreground = Paint.TextSecondary,
    };

    private static StackPanel Label(string glyph, string text)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal };
        panel.Children.Add(new TextBlock { Text = glyph, FontFamily = FontStack.Icons, FontSize = IconSize.S, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, Space.Px, Space.S3, 0) });
        panel.Children.Add(new TextBlock { Text = text, FontSize = TypeRamp.Caption, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center });
        return panel;
    }

    // ------------------------------------------------------------------ --snapshot

    /// <summary>Conversation d'exemple pour le rendu (rien n'est demandé à Claude).</summary>
    internal void ShowSample()
    {
        _sample =
        [
            new ClaudeTurn("C'est quoi la différence entre la RAM et un SSD ?")
            {
                Answer = "La **RAM** est la mémoire de travail : très rapide, mais vidée quand tu éteins le PC. Le **SSD** garde Windows, "
                         + "tes jeux et tes fichiers même éteint, mais il est plus lent que la RAM.\n\n- Plus de RAM : plus d'applis ouvertes sans ralentir.\n- Plus de SSD : plus de place.",
                Done = true,
            },
            new ClaudeTurn("Et 32 Go de RAM, c'est utile pour jouer ?") { Answer = "Pour la plupart des jeux, 16 Go suffisent ; 32 Go aident surtout si tu" },
        ];
        _shownCount = -1;
        Refresh();
    }
}
