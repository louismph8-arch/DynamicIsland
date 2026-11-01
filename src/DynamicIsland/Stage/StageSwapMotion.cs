using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Étapes d'un échange colonne ↔ centre, dans l'ordre où <see cref="StageManager"/> les fait.</summary>
internal enum StageSwapStep
{
    /// <summary>Animation de réduction / restauration de Windows coupée (sinon elle se superpose à la nôtre).</summary>
    HoldSystemAnimation,
    /// <summary>La fenêtre choisie, ENCORE RÉDUITE, reçoit sa place finale (SetWindowPlacement) : elle ne saute jamais.</summary>
    PlaceChosen,
    /// <summary>Images animées (miniatures DWM) posées sur les deux fenêtres, avant que rien ne bouge.</summary>
    ShowOverlay,
    /// <summary>L'ancienne fenêtre du centre est réduite SOUS son image (sans activer une voisine).</summary>
    MinimizeLeaving,
    /// <summary>La choisie grandit de la colonne vers sa place, l'ancienne rétrécit vers la colonne.</summary>
    Animate,
    /// <summary>La choisie est rendue (restaurée / agrandie) et passe au premier plan, UNE fois, sous son image.</summary>
    RestoreChosen,
    /// <summary>La colonne prend son nouvel état (la nouvelle miniature apparaît en fondu).</summary>
    UpdateColumn,
    /// <summary>L'image animée s'efface en ~100 ms sur la vraie fenêtre (passage invisible).</summary>
    FadeOverlay,
    /// <summary>Animation de Windows remise (si personne d'autre ne la veut coupée).</summary>
    ReleaseSystemAnimation,
}

/// <summary>
/// Calculs purs de l'échange animé de Stage Manager (vérifiés par l'auto-test) : courbe, rectangles intermédiaires, place finale,
/// opacités, ordre des étapes. Aucune fenêtre n'est touchée ici.
/// </summary>
internal static class StageSwapMotion
{
    /// <summary>Raideur du ressort (amorti critique : arrivée douce, aucun rebond).</summary>
    public const double Omega = 7.5;

    /// <summary>Après la restauration : images de suite où la fenêtre est déjà rendue avant d'effacer l'image animée.</summary>
    public const int RevealFrames = 3;

    /// <summary>Au plus ce temps d'attente de la fenêtre rendue (une appli lente ne bloque pas l'image animée).</summary>
    public static readonly TimeSpan RevealTimeout = TimeSpan.FromMilliseconds(320);

    /// <summary>
    /// Ressort à amortissement critique parti du repos, ramené à [0, 1] : x(t) = 1 − (1 + ωt)·e^(−ωt), divisé par x(1) pour finir
    /// EXACTEMENT à 1. Toujours croissant, jamais au-delà de 1 (pas de rebond visible).
    /// </summary>
    public static double Progress(double t)
    {
        t = Math.Clamp(t, 0, 1);
        static double X(double u) => 1 - (1 + Omega * u) * Math.Exp(-Omega * u);
        return X(t) / X(1);
    }

    public static RECT Lerp(RECT from, RECT to, double k) => new()
    {
        Left = Mix(from.Left, to.Left, k),
        Top = Mix(from.Top, to.Top, k),
        Right = Mix(from.Right, to.Right, k),
        Bottom = Mix(from.Bottom, to.Bottom, k),
    };

    private static int Mix(int a, int b, double k) => (int)Math.Round(a + (b - a) * k);

    /// <summary>Rectangle de l'image à l'instant <paramref name="t"/> (0 → 1 du temps de l'échange).</summary>
    public static RECT Frame(RECT from, RECT to, double t) => Lerp(from, to, Progress(t));

    /// <summary>Image au format <paramref name="aspect"/> centrée dans <paramref name="box"/> (comme la colonne dessine ses miniatures).</summary>
    public static RECT Fit(RECT box, double aspect)
    {
        double bw = box.Right - box.Left, bh = box.Bottom - box.Top;
        if (aspect <= 0 || bw <= 0 || bh <= 0) return box;
        double w = bw, h = w / aspect;
        if (h > bh)
        {
            h = bh;
            w = h * aspect;
        }
        int left = (int)Math.Round(box.Left + (bw - w) / 2), top = (int)Math.Round(box.Top + (bh - h) / 2);
        return new RECT { Left = left, Top = top, Right = left + (int)Math.Round(w), Bottom = top + (int)Math.Round(h) };
    }

    /// <summary>
    /// Fenêtre qui part : opacité de 255 à 72 % (colonne visible : elle y reste « en retrait ») ou jusqu'à 0 (colonne cachée : elle
    /// s'en va avec elle), surtout sur la seconde moitié.
    /// </summary>
    public static byte LeavingOpacity(double t, bool columnVisible)
    {
        double end = columnVisible ? StageShape.SwapLeavingOpacity : 0;
        double k = Progress(Math.Clamp((t - 0.25) / 0.75, 0, 1));
        return (byte)Math.Round(255 * (1 - (1 - end) * k));
    }

    /// <summary>Fondu de fin : de <paramref name="start"/> à 0 en <paramref name="f"/> (0 → 1).</summary>
    public static byte FadeOut(byte start, double f) => (byte)Math.Round(start * (1 - Math.Clamp(f, 0, 1)));

    /// <summary>Colonne cachée : là où file la fenêtre qui part (la miniature cliquée, poussée vers le bord gauche de l'écran).</summary>
    public static RECT Parking(RECT slot, RECT work)
    {
        int dx = slot.Right - work.Left;
        return new RECT { Left = slot.Left - dx, Top = slot.Top, Right = slot.Right - dx, Bottom = slot.Bottom };
    }

    /// <summary>Rectangle d'une fenêtre agrandie par Windows : la zone de travail plus son bord invisible tout autour.</summary>
    public static RECT Maximized(RECT work, int frame) => new()
    {
        Left = work.Left - frame, Top = work.Top - frame, Right = work.Right + frame, Bottom = work.Bottom + frame,
    };

    /// <summary>
    /// Place normale (fenêtre ENTIÈRE, bords invisibles compris) centrée dans la scène, calculée comme <see cref="StageManager"/>
    /// la vérifie ensuite (sur la partie visible) : la vérification de 300 ms n'a plus rien à corriger.
    /// </summary>
    public static RECT NormalPlace(RECT stage, int fullWidth, int fullHeight, Insets borders, bool resizable, bool fill)
    {
        int visibleW = Math.Max(1, fullWidth - borders.Left - borders.Right), visibleH = Math.Max(1, fullHeight - borders.Top - borders.Bottom);
        var visible = StageLayout.CenterPlace(stage, visibleW, visibleH, resizable, fill);
        return new RECT
        {
            Left = visible.Left - borders.Left, Top = visible.Top - borders.Top,
            Right = visible.Right + borders.Right, Bottom = visible.Bottom + borders.Bottom,
        };
    }

    /// <summary>Écran → repère de la zone de travail (rcNormalPosition de SetWindowPlacement).</summary>
    public static RECT ToWorkspace(RECT screen, POINT offset) => new()
    {
        Left = screen.Left - offset.X, Top = screen.Top - offset.Y, Right = screen.Right - offset.X, Bottom = screen.Bottom - offset.Y,
    };

    public static RECT FromWorkspace(RECT normal, POINT offset) => new()
    {
        Left = normal.Left + offset.X, Top = normal.Top + offset.Y, Right = normal.Right + offset.X, Bottom = normal.Bottom + offset.Y,
    };

    /// <summary>Écart par défaut (aucune fenêtre normale mesurée) : le repère de la zone de travail part de celle de l'écran principal.</summary>
    public static POINT DefaultWorkspaceOffset(RECT primaryWork, RECT primaryBounds) =>
        new() { X = primaryWork.Left - primaryBounds.Left, Y = primaryWork.Top - primaryBounds.Top };

    /// <summary>Échange animé seulement si Windows garde ses animations (« Effets d'animation ») et pas pendant un jeu sur cet écran.</summary>
    public static bool Animated(bool windowsAnimations, bool gamingOnScreen) => windowsAnimations && !gamingOnScreen;

    /// <summary>Ordre des étapes ; sans animation : mêmes étapes, sans image animée.</summary>
    public static IReadOnlyList<StageSwapStep> Steps(bool animated, bool hasLeaving)
    {
        var steps = new List<StageSwapStep> { StageSwapStep.HoldSystemAnimation, StageSwapStep.PlaceChosen };
        if (animated) steps.Add(StageSwapStep.ShowOverlay);
        if (hasLeaving) steps.Add(StageSwapStep.MinimizeLeaving);
        if (animated) steps.Add(StageSwapStep.Animate);
        steps.Add(StageSwapStep.RestoreChosen);
        steps.Add(StageSwapStep.UpdateColumn);
        if (animated) steps.Add(StageSwapStep.FadeOverlay);
        steps.Add(StageSwapStep.ReleaseSystemAnimation);
        return steps;
    }
}

/// <summary>Bords invisibles d'une fenêtre (fenêtre entière moins sa partie visible), en pixels.</summary>
internal readonly record struct Insets(int Left, int Top, int Right, int Bottom)
{
    public static Insets Between(RECT window, RECT visible) => new(
        Math.Max(0, visible.Left - window.Left), Math.Max(0, visible.Top - window.Top),
        Math.Max(0, window.Right - visible.Right), Math.Max(0, window.Bottom - visible.Bottom));
}
