using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Shapes;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Curseur fin (piste de <see cref="Sizes.Bar"/> px, pastille au survol) : clic ou glisser n'importe où,
/// molette = ±5 %. Marche dans l'île, qui ne prend jamais le focus (souris seulement).
/// </summary>
public sealed class LineSlider : Grid
{
    private const double WheelStep = 0.05;
    private readonly SquircleBorder _fill = new()
    {
        Height = Sizes.Bar,
        Width = 0,
        CornerRadius = Corners.Pill,
        Background = Paint.White,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
    };
    private readonly Ellipse _knob = new()
    {
        Width = Sizes.LineKnob,
        Height = Sizes.LineKnob,
        Fill = Paint.White,
        HorizontalAlignment = HorizontalAlignment.Left,
        VerticalAlignment = VerticalAlignment.Center,
        IsHitTestVisible = false,
        Opacity = 0,
    };
    private double _value;

    public LineSlider()
    {
        Height = Sizes.SliderThumb; // zone cliquable plus haute que la piste
        Background = Paint.HitTarget;
        Cursor = Cursors.Hand;
        Children.Add(new SquircleBorder
        {
            Height = Sizes.Bar,
            CornerRadius = Corners.Pill,
            Background = Paint.Selected,
            VerticalAlignment = VerticalAlignment.Center,
        });
        Children.Add(_fill);
        Children.Add(_knob);
        SizeChanged += (_, _) => Place();
        MouseEnter += (_, _) => _knob.Opacity = 1;
        MouseLeave += (_, _) =>
        {
            if (!IsMouseCaptured) _knob.Opacity = 0;
        };
        IsEnabledChanged += (_, _) => Opacity = IsEnabled ? 1 : Interaction.DisabledOpacity;
    }

    /// <summary>Valeur de 0 à 1.</summary>
    public double Value
    {
        get => _value;
        set
        {
            _value = Math.Clamp(value, 0, 1);
            Place();
        }
    }

    /// <summary>Levé uniquement quand l'utilisateur change la valeur (pas lors d'un réglage par code).</summary>
    public event Action<double>? UserChanged;

    public bool IsDragging => IsMouseCaptured;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        CaptureMouse();
        SetFromPointer(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (IsMouseCaptured) SetFromPointer(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (IsMouseCaptured) ReleaseMouseCapture();
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsMouseOver) _knob.Opacity = 0;
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        SetByUser(_value + Math.Sign(e.Delta) * WheelStep);
        e.Handled = true;
    }

    private void SetFromPointer(MouseEventArgs e) => SetByUser(e.GetPosition(this).X / Math.Max(ActualWidth, 1));

    private void SetByUser(double value)
    {
        value = Math.Clamp(value, 0, 1);
        if (Math.Abs(value - _value) < 0.001) return;
        Value = value;
        UserChanged?.Invoke(_value);
    }

    private void Place()
    {
        double width = ActualWidth;
        _fill.Width = width * _value;
        double left = Math.Clamp(width * _value - Sizes.LineKnob / 2, 0, Math.Max(0, width - Sizes.LineKnob));
        _knob.Margin = new Thickness(left, 0, 0, 0);
    }
}
