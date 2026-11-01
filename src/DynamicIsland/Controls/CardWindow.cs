using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using DynamicIsland.Services;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Fenêtre « carte » d'Opaline (7C : avis, nouveautés) : carte squircle au fond de l'île, verre liquide, halo d'accent,
/// apparition en se déployant et fermeture en fondu, comme l'assistant de bienvenue. Activable (on peut y taper),
/// déplaçable par son fond, Échap = fermer. Les classes filles remplissent <see cref="Body"/> et le pied de page.
/// </summary>
public abstract class CardWindow : Window
{
    protected static readonly IEasingFunction EaseOut = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Overshoot = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.35 };

    private readonly Grid _shell = new();
    private readonly ScaleTransform _scale = new(1, 1);
    private readonly DropShadowEffect _shadow = new() { RenderingBias = RenderingBias.Performance, Direction = Shadows.Direction };
    private readonly LiquidGlass _glass;
    private bool _closing;

    protected readonly SquircleBorder Card = new();
    /// <summary>Contenu de la carte (au-dessus du pied de page).</summary>
    protected readonly Grid Body = new();
    /// <summary>Pied de page : boutons à droite, texte d'état à gauche.</summary>
    protected readonly Grid Footer = new() { Margin = new Thickness(Space.S14, Space.S6, Space.S14, Space.S10) };

    /// <summary>Rendu --snapshot : hors écran, sans animation ni action.</summary>
    protected bool Snapshot { get; }

    protected CardWindow(string title, double width, double height, bool snapshot)
    {
        Snapshot = snapshot;
        Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Settings/SettingsStyles.xaml") });
        Title = title;
        Icon = OpalineLogo.Get(64);
        Width = width;
        Height = Math.Min(height, SystemParameters.WorkArea.Height - Space.S10); // petit écran ou grande mise à l'échelle : le contenu défile
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        FontFamily = FontStack.Ui;
        Foreground = Paint.TextPrimary;
        UseLayoutRounding = true;
        if (snapshot)
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = -30000;
            Top = 0;
            ShowActivated = false;
        }

        Card.CornerRadius = new CornerRadius(AppearanceService.Radius(Radius.Palette));
        Card.BorderThickness = Borders.Hairline;
        Card.SetResourceReference(Border.BackgroundProperty, AppearanceService.IslandBrushKey);
        Card.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ButtonState == MouseButtonState.Pressed && !Snapshot) DragMove(); // la carte se déplace par son fond
        };
        var layout = new Grid();
        layout.RowDefinitions.Add(new RowDefinition());
        layout.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        Grid.SetRow(Footer, 1);
        layout.Children.Add(Body);
        layout.Children.Add(Footer);
        Card.Child = layout;

        AppearanceService.ApplyGlow(_shadow, Shadows.Window);
        _shell.Effect = _shadow;
        _shell.Margin = new Thickness(WelcomeShape.Margin);
        _shell.RenderTransformOrigin = new Point(0.5, 0.5);
        _shell.RenderTransform = _scale;
        _shell.Children.Add(Card);
        Content = new Grid { Children = { _shell } };
        _glass = new LiquidGlass(Card, () => Card.CornerRadius.TopLeft);
        _ = new Design.OpalSkin(Card, Design.OpalRole.Panel, _glass); // verre opalin (O1), détaché à la fermeture
        Closed += (_, _) => _glass.Detach();

        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            CloseAnimated();
            e.Handled = true;
        };
        Loaded += (_, _) =>
        {
            if (Snapshot) return;
            _shell.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long) { EasingFunction = EaseOut });
            var grow = new DoubleAnimation(0.92, 1, Motion.Page) { EasingFunction = Overshoot };
            _scale.BeginAnimation(ScaleTransform.ScaleXProperty, grow);
            _scale.BeginAnimation(ScaleTransform.ScaleYProperty, grow);
        };
    }

    /// <summary>Fermeture en fondu (rétrécit un peu, comme la palette qui rentre dans l'île).</summary>
    protected void CloseAnimated()
    {
        if (_closing) return;
        _closing = true;
        if (Snapshot)
        {
            Close();
            return;
        }
        var fade = new DoubleAnimation(0, Motion.Swift) { EasingFunction = EaseOut };
        fade.Completed += (_, _) => Close();
        _shell.BeginAnimation(OpacityProperty, fade);
        var shrink = new DoubleAnimation(0.96, Motion.Swift) { EasingFunction = EaseOut };
        _scale.BeginAnimation(ScaleTransform.ScaleXProperty, shrink);
        _scale.BeginAnimation(ScaleTransform.ScaleYProperty, shrink);
    }

    /// <summary>Apparition d'un élément après les précédents : fondu + petite montée (rien en --snapshot).</summary>
    protected void Reveal(UIElement element, int index)
    {
        if (Snapshot) return;
        var move = new TranslateTransform(0, CardShape.Rise);
        element.RenderTransform = move;
        element.Opacity = 0;
        var begin = TimeSpan.FromSeconds(CardShape.FirstReveal + index * CardShape.Stagger);
        element.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, Motion.Long) { BeginTime = begin, EasingFunction = EaseOut });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(CardShape.Rise, 0, Motion.Page) { BeginTime = begin, EasingFunction = EaseOut });
    }

    /// <summary>
    /// Fenêtre redimensionnable (éditeur de note) : un petit coin en bas à droite de la carte (la fenêtre transparente n'a pas
    /// de bord que Windows saurait tirer).
    /// </summary>
    protected void EnableResize(double minWidth, double minHeight)
    {
        MinWidth = minWidth;
        MinHeight = Math.Min(minHeight, Height);
        var glyph = new FrameworkElementFactory(typeof(TextBlock));
        glyph.SetValue(TextBlock.TextProperty, Icons.ResizeGrip);
        glyph.SetValue(TextBlock.FontFamilyProperty, FontStack.Icons);
        glyph.SetValue(TextBlock.FontSizeProperty, IconSize.S);
        glyph.SetValue(TextBlock.ForegroundProperty, Paint.TextTertiary);
        glyph.SetValue(HorizontalAlignmentProperty, HorizontalAlignment.Center);
        glyph.SetValue(VerticalAlignmentProperty, VerticalAlignment.Center);
        var area = new FrameworkElementFactory(typeof(Border));
        area.SetValue(Border.BackgroundProperty, Paint.HitTarget);
        area.AppendChild(glyph);
        var grip = new System.Windows.Controls.Primitives.Thumb
        {
            Width = CardShape.Grip, Height = CardShape.Grip, Cursor = Cursors.SizeNWSE, ToolTip = Loc.T("card.resize"),
            HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, Space.S3, Space.S3),
            Template = new ControlTemplate(typeof(System.Windows.Controls.Primitives.Thumb)) { VisualTree = area },
        };
        grip.DragDelta += (_, e) =>
        {
            Width = Math.Max(MinWidth, Width + e.HorizontalChange);
            Height = Math.Max(MinHeight, Math.Min(SystemParameters.WorkArea.Height, Height + e.VerticalChange));
        };
        var layout = (Grid)Card.Child;
        Grid.SetRowSpan(grip, 2);
        layout.Children.Add(grip);
    }

    // ------------------------------------------------------------------ petits éléments

    protected static TextBlock Heading(string text, double size = TypeRamp.LargeTitle) => new()
    {
        Text = text,
        FontFamily = FontStack.Display,
        FontSize = size,
        FontWeight = FontWeights.SemiBold,
        TextWrapping = TextWrapping.Wrap,
    };

    protected static TextBlock Paragraph(string text) => new()
    {
        Text = text,
        FontSize = TypeRamp.Body,
        Foreground = Paint.TextSecondary,
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, Space.S2, 0, 0),
    };

    /// <summary>Bouton principal (dégradé d'accent).</summary>
    protected Button PrimaryButton(string text)
    {
        var button = new Button
        {
            Style = (Style)FindResource("RoundButton"),
            Content = new TextBlock { Text = text, FontSize = TypeRamp.Body, FontWeight = FontWeights.SemiBold, Foreground = Paint.White },
            MinWidth = CardShape.ButtonWidth,
            Height = Sizes.SoftButton,
            Padding = new Thickness(Space.S8, 0, Space.S8, 0),
        };
        button.SetResourceReference(BackgroundProperty, AppearanceService.AccentGradientKey);
        return button;
    }

    protected Button SecondaryButton(string text) => new()
    {
        Style = (Style)FindResource("SettingsButton"),
        Content = text,
        VerticalAlignment = VerticalAlignment.Center,
    };
}
