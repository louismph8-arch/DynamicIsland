using DynamicIsland.Themes;

namespace DynamicIsland.Animation;

/// <summary>
/// Ressort amorti (masse = 1) intégré à petits pas fixes. C'est lui qui donne l'effet
/// « rebond » de la Dynamic Island : un amortissement &lt; 1 dépasse légèrement la cible
/// avant de s'y poser.
/// </summary>
public sealed class Spring(double value)
{
    public double Value { get; private set; } = value;
    public double Velocity { get; private set; }
    public double Target { get; private set; } = value;
    public double Stiffness { get; private set; } = 260;
    public double DampingRatio { get; private set; } = 0.8;

    public bool IsSettled => Value == Target && Velocity == 0;

    /// <summary>Nouvelle cible avec un ressort des tokens (Themes/Tokens : Springs).</summary>
    public void SetTarget(double target, SpringSpec spec) => SetTarget(target, spec.Stiffness, spec.Damping);

    public void SetTarget(double target, double stiffness, double dampingRatio)
    {
        Target = target;
        Stiffness = stiffness;
        DampingRatio = dampingRatio;
    }

    /// <summary>Saute directement à la cible (sans animation).</summary>
    public void Snap()
    {
        Value = Target;
        Velocity = 0;
    }

    /// <summary>Avance la simulation de <paramref name="dt"/> secondes. Renvoie true une fois posé.</summary>
    public bool Step(double dt)
    {
        if (IsSettled) return true;

        const double MaxSubStep = 1.0 / 480;
        int steps = Math.Max(1, (int)Math.Ceiling(dt / MaxSubStep));
        double h = dt / steps;
        double damping = 2 * DampingRatio * Math.Sqrt(Stiffness);

        for (int i = 0; i < steps; i++)
        {
            double acceleration = -Stiffness * (Value - Target) - damping * Velocity;
            Velocity += acceleration * h;
            Value += Velocity * h;
        }

        if (Math.Abs(Value - Target) < 0.05 && Math.Abs(Velocity) < 1)
        {
            Value = Target;
            Velocity = 0;
            return true;
        }
        return false;
    }
}
