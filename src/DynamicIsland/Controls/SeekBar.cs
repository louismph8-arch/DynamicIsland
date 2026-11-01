using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using DynamicIsland.Themes;

namespace DynamicIsland.Controls;

/// <summary>
/// Barre de position d'un morceau (onglet Musique) : clic = aller là, glisser = prévisualiser puis lâcher = aller là, molette =
/// ±5 s (<see cref="Wheeled"/>). Souris seulement (l'île ne prend jamais le clavier). <see cref="Interactive"/> faux = simple
/// barre de progression (le lecteur ne permet pas de changer la position).
/// </summary>
public sealed class SeekBar : Grid
{
    private readonly SquircleBorder _fill = new()
    {
        Height = Sizes.Bar,
        Width = 0,
        CornerRadius = Corners.Pill,
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
    private bool _interactive = true;

    public SeekBar()
    {
        Height = Sizes.SliderThumb; // zone cliquable plus haute que la piste
        Background = Paint.HitTarget;
        var track = new SquircleBorder
        {
            Height = Sizes.Bar,
            CornerRadius = Corners.Pill,
            VerticalAlignment = VerticalAlignment.Center,
        };
        track.SetResourceReference(Border.BackgroundProperty, "Paint.Selected");
        _fill.SetResourceReference(Border.BackgroundProperty, "Paint.TextPrimary");
        Children.Add(track);
        Children.Add(_fill);
        Children.Add(_knob);
        Cursor = Cursors.Hand;
        SizeChanged += (_, _) => Place();
        MouseEnter += (_, _) => _knob.Opacity = _interactive ? 1 : 0;
        MouseLeave += (_, _) =>
        {
            if (!IsMouseCaptured) _knob.Opacity = 0;
        };
    }

    /// <summary>Partie remplie, de 0 à 1 (réglée par le code ; ignorée pendant un glisser).</summary>
    public double Value
    {
        get => _value;
        set
        {
            if (IsDragging) return;
            _value = Math.Clamp(value, 0, 1);
            Place();
        }
    }

    /// <summary>Couleur de la partie remplie (couleur de la pochette).</summary>
    public Brush FillBrush
    {
        set => _fill.Background = value;
    }

    /// <summary>Faux : barre non cliquable (pas de pastille, pas de molette).</summary>
    public bool Interactive
    {
        get => _interactive;
        set
        {
            _interactive = value;
            Cursor = value ? Cursors.Hand : Cursors.Arrow;
            if (!value)
            {
                if (IsMouseCaptured) ReleaseMouseCapture();
                _knob.Opacity = 0;
            }
        }
    }

    public bool IsDragging => IsMouseCaptured;

    /// <summary>Pendant un glisser : fraction sous la souris (pour afficher le temps visé).</summary>
    public event Action<double>? Previewing;

    /// <summary>Clic ou lâcher : fraction choisie.</summary>
    public event Action<double>? Committed;

    /// <summary>Molette : +1 vers le haut, −1 vers le bas.</summary>
    public event Action<int>? Wheeled;

    /// <summary>Fin d'un glisser (lâché ou annulé), après <see cref="Committed"/>.</summary>
    public event Action? DragEnded;

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        if (!_interactive) return;
        CaptureMouse();
        Follow(e);
        e.Handled = true;
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);
        if (_interactive && IsMouseCaptured) Follow(e);
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        if (!IsMouseCaptured) return;
        Follow(e);
        double chosen = _value;
        ReleaseMouseCapture(); // → OnLostMouseCapture
        Committed?.Invoke(chosen);
        e.Handled = true;
    }

    protected override void OnLostMouseCapture(MouseEventArgs e)
    {
        base.OnLostMouseCapture(e);
        if (!IsMouseOver) _knob.Opacity = 0;
        DragEnded?.Invoke();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        if (!_interactive || e.Delta == 0) return;
        Wheeled?.Invoke(Math.Sign(e.Delta));
        e.Handled = true;
    }

    private void Follow(MouseEventArgs e)
    {
        _value = Math.Clamp(e.GetPosition(this).X / Math.Max(ActualWidth, 1), 0, 1);
        Place();
        Previewing?.Invoke(_value);
    }

    private void Place()
    {
        double width = ActualWidth;
        _fill.Width = width * _value;
        double left = Math.Clamp(width * _value - Sizes.LineKnob / 2, 0, Math.Max(0, width - Sizes.LineKnob));
        _knob.Margin = new Thickness(left, 0, 0, 0);
    }
}
