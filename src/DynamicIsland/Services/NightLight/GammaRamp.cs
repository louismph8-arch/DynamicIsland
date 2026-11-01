namespace DynamicIsland.Services.NightLight;

/// <summary>
/// Calculs de la veilleuse (7F), tous purs (vérifiés par --self-test) : température de couleur, rampe gamma chaude à partir de
/// la rampe d'origine de l'écran (sa calibration est gardée), limite de Windows, et contrôle d'une rampe avant de l'écrire.
/// Une rampe = 3 × 256 valeurs 16 bits (rouge, vert, bleu), comme <c>SetDeviceGammaRamp</c> les attend.
/// </summary>
public static class GammaRamp
{
    public const int Size = 256;
    public const int Length = 3 * Size;
    public const double NeutralKelvin = 6500;
    public const double WarmestKelvin = 2700;
    /// <summary>
    /// Écart maximal que Windows accepte entre une valeur et la rampe neutre, en 1/256 de la plage (au-delà, SetDeviceGammaRamp
    /// refuse sans registre « GdiICMGammaRange », qu'on ne touche pas) : ≈ 3 300 K au plus chaud sur le bleu. Si Windows refuse
    /// quand même, l'intensité est encore réduite (voir NightLightService).
    /// </summary>
    public const int WindowsMaxDeviation = 128;

    /// <summary>Rampe neutre (i × 257 : 0 → 0, 255 → 65535).</summary>
    public static ushort[] Identity()
    {
        var ramp = new ushort[Length];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < Size; i++) ramp[c * Size + i] = (ushort)(i * 257);
        return ramp;
    }

    /// <summary>Intensité 0–1 → température : 0 = 6500 K, 1 = 2700 K.</summary>
    public static double Kelvin(double strength) => NeutralKelvin - (NeutralKelvin - WarmestKelvin) * Math.Clamp(strength, 0, 1);

    /// <summary>
    /// Part du rouge, du vert et du bleu à cette température (approximation du corps noir de Tanner Helland), ramenée à 1 pour
    /// 6500 K : plus chaud = moins de bleu, un peu moins de vert.
    /// </summary>
    public static (double R, double G, double B) Multipliers(double kelvin)
    {
        var raw = Blackbody(kelvin);
        var neutral = Blackbody(NeutralKelvin);
        return (Math.Min(1, raw.R / neutral.R), Math.Min(1, raw.G / neutral.G), Math.Min(1, raw.B / neutral.B));
    }

    private static (double R, double G, double B) Blackbody(double kelvin)
    {
        double t = Math.Clamp(kelvin, 1000, 40000) / 100;
        double r = t <= 66 ? 255 : 329.698727446 * Math.Pow(t - 60, -0.1332047592);
        double g = t <= 66 ? 99.4708025861 * Math.Log(t) - 161.1195681661 : 288.1221695283 * Math.Pow(t - 60, -0.0755148492);
        double b = t >= 66 ? 255 : t <= 19 ? 0 : 138.5177312231 * Math.Log(t - 10) - 305.0447927307;
        return (Math.Clamp(r, 1, 255), Math.Clamp(g, 1, 255), Math.Clamp(b, 1, 255));
    }

    /// <summary>Rampe chaude : la rampe d'origine multipliée, canal par canal (la calibration de l'écran est gardée).</summary>
    public static ushort[] Build(ushort[] original, double strength)
    {
        var (r, g, b) = Multipliers(Kelvin(strength));
        var factors = new[] { r, g, b };
        var ramp = new ushort[Length];
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < Size; i++)
                ramp[c * Size + i] = (ushort)Math.Clamp(Math.Round(original[c * Size + i] * factors[c]), 0, ushort.MaxValue);
        return ramp;
    }

    /// <summary>Dans la plage acceptée par Windows (écart à la rampe neutre ≤ <see cref="WindowsMaxDeviation"/>).</summary>
    public static bool WithinWindowsLimit(ushort[] ramp)
    {
        if (ramp.Length != Length) return false;
        for (int c = 0; c < 3; c++)
            for (int i = 0; i < Size; i++)
                if (Math.Abs((ramp[c * Size + i] >> 8) - i) > WindowsMaxDeviation) return false;
        return true;
    }

    /// <summary>Plus forte intensité (0–1, au centième) que Windows acceptera pour cette rampe d'origine.</summary>
    public static double MaxStrength(ushort[] original)
    {
        if (WithinWindowsLimit(Build(original, 1))) return 1;
        double low = 0, high = 1;
        for (int step = 0; step < 12; step++)
        {
            double mid = (low + high) / 2;
            if (WithinWindowsLimit(Build(original, mid))) low = mid;
            else high = mid;
        }
        return Math.Floor(low * 100) / 100;
    }

    /// <summary>
    /// Rampe plausible pour être écrite : bonne taille, croissante sur chaque canal, et blanc pas trop sombre. Une rampe lue
    /// bizarre (pilote qui rend des zéros) ne doit jamais être réécrite : l'écran deviendrait noir.
    /// </summary>
    public static bool IsSane(ushort[]? ramp)
    {
        if (ramp is not { Length: Length }) return false;
        for (int c = 0; c < 3; c++)
        {
            for (int i = 1; i < Size; i++)
                if (ramp[c * Size + i] < ramp[c * Size + i - 1]) return false;
            if (ramp[c * Size + Size - 1] < 0x4000) return false;
        }
        return true;
    }

    /// <summary>Rampe d'origine à garder : celle lue si elle est plausible, sinon la rampe neutre.</summary>
    public static ushort[] OriginalOrIdentity(ushort[]? read) => IsSane(read) ? (ushort[])read!.Clone() : Identity();

    public static string Encode(ushort[] ramp)
    {
        var bytes = new byte[ramp.Length * 2];
        Buffer.BlockCopy(ramp, 0, bytes, 0, bytes.Length);
        return Convert.ToBase64String(bytes);
    }

    public static ushort[]? Decode(string text)
    {
        try
        {
            var bytes = Convert.FromBase64String(text.Trim());
            if (bytes.Length != Length * 2) return null;
            var ramp = new ushort[Length];
            Buffer.BlockCopy(bytes, 0, ramp, 0, bytes.Length);
            return ramp;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    public static bool Same(ushort[] a, ushort[] b) => a.Length == b.Length && a.AsSpan().SequenceEqual(b);
}
