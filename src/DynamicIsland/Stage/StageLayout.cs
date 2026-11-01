using DynamicIsland.Themes;
using static DynamicIsland.Interop.NativeMethods;

namespace DynamicIsland.Stage;

/// <summary>Pourquoi une fenêtre reste hors de Stage Manager (laissée telle quelle).</summary>
internal enum StageExclusion
{
    None,
    /// <summary>Opaline elle-même (paramètres, assistant…).</summary>
    Own,
    /// <summary>Fenêtre outil, sans bouton « Réduire » (boîte de dialogue, petite fenêtre flottante).</summary>
    Tool,
    /// <summary>« Toujours au premier plan ».</summary>
    Topmost,
    /// <summary>Plein écran (vidéo) ou jeu.</summary>
    Fullscreen,
    /// <summary>Appli lancée en administrateur : Windows interdit de la déplacer depuis une appli normale (UIPI).</summary>
    Elevated,
    /// <summary>« Stage Manager : ignorer » dans les règles par application.</summary>
    Ignored,
}

/// <summary>Ce qu'on sait d'une fenêtre pour décider si Stage Manager s'en occupe (calcul pur, vérifié par --self-test).</summary>
internal readonly record struct StageFlags(bool Own, bool ToolWindow, bool Minimizable, bool Topmost, bool Fullscreen, bool Game,
    bool Elevated, bool Ignored);

/// <summary>Un groupe de la colonne : les fenêtres d'une même appli (la plus récente d'abord).</summary>
internal sealed record StageGroup(string Key, IReadOnlyList<IntPtr> Windows);

/// <summary>
/// Décisions pures de Stage Manager (7G), sans Windows : écran d'une fenêtre, place de la fenêtre au centre, groupes de la colonne,
/// exclusions. Tout est en pixels physiques ; vérifié avec des écrans simulés par --self-test.
/// </summary>
internal static class StageLayout
{
    /// <summary>
    /// Écran d'une fenêtre : celui qui en contient la plus grande part, sinon (fenêtre hors de tous les écrans) le plus proche de son
    /// centre ; -1 s'il n'y a aucun écran.
    /// </summary>
    public static int ScreenFor(RECT window, IReadOnlyList<RECT> screens)
    {
        int best = -1;
        long bestArea = 0;
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            long w = Math.Min(window.Right, s.Right) - Math.Max(window.Left, s.Left);
            long h = Math.Min(window.Bottom, s.Bottom) - Math.Max(window.Top, s.Top);
            long area = w > 0 && h > 0 ? w * h : 0;
            if (area > bestArea)
            {
                bestArea = area;
                best = i;
            }
        }
        if (best >= 0 || screens.Count == 0) return best;
        double cx = (window.Left + window.Right) / 2.0, cy = (window.Top + window.Bottom) / 2.0;
        double bestDistance = double.MaxValue;
        for (int i = 0; i < screens.Count; i++)
        {
            var s = screens[i];
            double dx = Math.Max(Math.Max(s.Left - cx, 0), cx - s.Right);
            double dy = Math.Max(Math.Max(s.Top - cy, 0), cy - s.Bottom);
            double distance = dx * dx + dy * dy;
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = i;
            }
        }
        return best;
    }

    /// <summary>Largeur réservée à la colonne (bord de l'écran + colonne + écart avec la scène), en pixels pour cette échelle.</summary>
    public static int ColumnReserve(double scale) =>
        (int)Math.Round((StageShape.ScreenMargin + StageShape.ColumnWidth + StageShape.StageGap) * scale);

    /// <summary>La scène : la zone de travail moins la colonne à gauche (et une petite marge ailleurs).</summary>
    public static RECT StageArea(RECT work, double scale)
    {
        int margin = (int)Math.Round(StageShape.ScreenMargin * scale);
        var stage = new RECT
        {
            Left = work.Left + ColumnReserve(scale),
            Top = work.Top + margin,
            Right = work.Right - margin,
            Bottom = work.Bottom - margin,
        };
        if (stage.Right - stage.Left < 200) stage.Left = Math.Max(work.Left, stage.Right - 200); // écran minuscule : on garde une scène
        return stage;
    }

    /// <summary>
    /// Place (partie visible) de la fenêtre au centre : sa taille si elle tient dans la scène, sinon ramenée à 80 % de la scène
    /// (seulement si elle se redimensionne) ; une fenêtre agrandie remplit la scène. Toujours centrée ; une fenêtre trop grande qui
    /// refuse de rapetisser est collée en haut à gauche de la scène (sa barre de titre reste atteignable).
    /// </summary>
    public static RECT CenterPlace(RECT stage, int width, int height, bool resizable, bool maximized)
    {
        int stageW = stage.Right - stage.Left, stageH = stage.Bottom - stage.Top;
        int w = width, h = height;
        if (maximized && resizable)
        {
            w = stageW;
            h = stageH;
        }
        else if (resizable)
        {
            if (w > stageW) w = (int)Math.Round(stageW * StageShape.Shrink);
            if (h > stageH) h = (int)Math.Round(stageH * StageShape.Shrink);
        }
        w = Math.Max(1, w);
        h = Math.Max(1, h);
        int x = stage.Left + (stageW - w) / 2, y = stage.Top + (stageH - h) / 2;
        if (w > stageW) x = stage.Left;
        if (h > stageH) y = stage.Top;
        return new RECT { Left = x, Top = y, Right = x + w, Bottom = y + h };
    }

    /// <summary>Combien de groupes tiennent dans la hauteur de la zone de travail (6 au plus, 1 au moins).</summary>
    public static int MaxGroups(int workHeightPx, double scale)
    {
        double available = workHeightPx / Math.Max(0.5, scale) - 2 * StageShape.ScreenMargin;
        return Math.Clamp((int)(available / (StageShape.GroupHeight + StageShape.GroupGap)), 1, StageShape.MaxGroups);
    }

    /// <summary>
    /// Groupes de la colonne : fenêtres de l'écran SAUF celle du centre, groupées par appli (même clé que le dock : exe ou appli
    /// du Store), dans l'ordre d'utilisation (la plus récente d'abord, ses autres fenêtres derrière elle), <paramref name="max"/> au plus.
    /// </summary>
    public static List<StageGroup> Groups(IEnumerable<(IntPtr Handle, string Key)> recentFirst, IntPtr center, int max)
    {
        var order = new List<string>();
        var windows = new Dictionary<string, List<IntPtr>>();
        foreach (var (handle, key) in recentFirst)
        {
            if (handle == center || handle == IntPtr.Zero) continue;
            if (!windows.TryGetValue(key, out var list))
            {
                windows[key] = list = [];
                order.Add(key);
            }
            if (!list.Contains(handle)) list.Add(handle);
        }
        return order.Take(Math.Max(0, max)).Select(k => new StageGroup(k, windows[k])).ToList();
    }

    /// <summary>Stage Manager s'occupe-t-il de cette fenêtre ? Première raison de la laisser tranquille, sinon None.</summary>
    public static StageExclusion Exclusion(StageFlags f) =>
        f.Own ? StageExclusion.Own
        : f.Ignored ? StageExclusion.Ignored
        : f.ToolWindow || !f.Minimizable ? StageExclusion.Tool
        : f.Topmost ? StageExclusion.Topmost
        : f.Fullscreen || f.Game ? StageExclusion.Fullscreen
        : f.Elevated ? StageExclusion.Elevated
        : StageExclusion.None;

    /// <summary>
    /// La fenêtre au premier plan devient-elle la fenêtre au centre de son écran ? Seulement une fenêtre gérée (pas exclue), qui n'y
    /// est pas déjà, et jamais pendant un jeu sur cet écran.
    /// </summary>
    public static bool TakesCenter(bool managed, IntPtr window, IntPtr currentCenter, bool gamingOnScreen) =>
        managed && window != IntPtr.Zero && window != currentCenter && !gamingOnScreen;

    /// <summary>Clé d'appli d'une fenêtre (comme le dock) : appli du Store, sinon l'exe, sinon la fenêtre seule.</summary>
    public static string AppKey(string? appId, string? exePath, IntPtr handle) =>
        !string.IsNullOrEmpty(appId) ? "app:" + appId.ToLowerInvariant()
        : !string.IsNullOrEmpty(exePath) ? "exe:" + exePath.ToLowerInvariant()
        : "win:" + handle.ToInt64();

    /// <summary>Rectangle de la fenêtre entière pour une partie visible voulue (bords invisibles de Windows 11 ajoutés).</summary>
    public static RECT WithBorders(RECT visible, RECT windowRect, RECT visibleRect) => new()
    {
        Left = visible.Left - (visibleRect.Left - windowRect.Left),
        Top = visible.Top - (visibleRect.Top - windowRect.Top),
        Right = visible.Right + (windowRect.Right - visibleRect.Right),
        Bottom = visible.Bottom + (windowRect.Bottom - visibleRect.Bottom),
    };
}
