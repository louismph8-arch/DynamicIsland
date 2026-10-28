using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>Curseur « pilule » façon centre de contrôle iOS : on clique/glisse n'importe où, ou molette.</summary>
public partial class FillSlider : UserControl
{
    public static readonly DependencyProperty ValueProperty = DependencyProperty.Register(
        nameof(Value), typeof(double), typeof(FillSlider),
        new PropertyMetadata(0.0, (d, _) => ((FillSlider)d).UpdateFill(), (_, v) => Math.Clamp((double)v, 0.0, 100.0)));

    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(FillSlider),
        new PropertyMetadata("", (d, e) => ((FillSlider)d).SetIcon((string)e.NewValue)));

    public static readonly DependencyProperty LabelProperty = DependencyProperty.Register(
        nameof(Label), typeof(string), typeof(FillSlider),
        new PropertyMetadata("", (d, e) => ((FillSlider)d).SetLabel((string)e.NewValue)));

    private readonly RectangleGeometry _fillClip = new();
    private Size _clipSize;

    public FillSlider()
    {
        InitializeComponent();
        DarkLayer.Clip = _fillClip;
        SizeChanged += (_, _) => UpdateFill();
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : Interaction.DimmedOpacity;
        UpdateFill();
    }

    /// <summary>Levé uniquement quand l'utilisateur change la valeur (pas lors d'un réglage par code).</summary>
    public event Action<double>? UserChanged;

    /// <summary>Valeur de 0 à 100.</summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    public string Icon
    {
        get => (string)GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public bool IsDragging => IsMouseCaptured;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!IsEnabled) return;
        CaptureMouse();
        SetFromPointer(e.GetPosition(this).X);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SetFromPointer(e.GetPosition(this).X);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!IsEnabled) return;
        SetByUser(Value + (e.Delta > 0 ? 4 : -4));
        e.Handled = true;
    }

    private void SetFromPointer(double x) => SetByUser(x / Math.Max(ActualWidth, 1) * 100);

    private void SetByUser(double value)
    {
        value = Math.Clamp(value, 0, 100);
        if (Math.Abs(value - Value) < 0.01) return;
        Value = value;
        UserChanged?.Invoke(Value);
    }

    private void SetIcon(string icon) => IconLight.Text = IconDark.Text = icon;

    private void SetLabel(string label) => LabelLight.Text = LabelDark.Text = label;

    private void UpdateFill()
    {
        double width = ActualWidth, height = ActualHeight;
        double fill = width * Value / 100;
        if (_clipSize != new Size(width, height))
        {
            _clipSize = new Size(width, height);
            Root.Clip = SquircleGeometry.Create(new Rect(_clipSize), Corners.XXL, Radius.Smoothing);
        }
        _fillClip.Rect = new Rect(0, 0, fill, height);
        Fill.Width = fill;
        PercentLight.Text = PercentDark.Text = Loc.F("format.percent", Math.Round(Value));
    }
}
