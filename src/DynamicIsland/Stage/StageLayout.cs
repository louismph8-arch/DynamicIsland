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
    /// <summary>Boîte de dialogue, menu, fenêtre possédée par une autre, écran de démarrage, petite fenêtre fixe : jamais au centre.</summary>
    Popup,
    /// <summary>Hors de tout écran (rendu hors écran), « cloaked », transparente aux clics ou minuscule.</summary>
    Invisible,
}

/// <summary>Ce qu'on sait d'une fenêtre pour décider si Stage Manager s'en occupe (calcul pur, vérifié par --self-test).</summary>
/// <param name="Popup">Voir <see cref="StageLayout.Shape"/> : dialogue, menu, fenêtre possédée…</param>
/// <param name="Invisible">Voir <see cref="StageLayout.Shape"/> : hors écran, cloaked, transparente, minuscule.</param>
internal readonly record struct StageFlags(bool Own, bool ToolWindow, bool Minimizable, bool Topmost, bool Fullscreen, bool Game,
    bool Elevated, bool Ignored, bool Popup = false, bool Invisible = false);

/// <summary>
/// Ce qu'on lit d'une fenêtre (styles bruts, propriétaire, place) pour savoir si c'est une VRAIE fenêtre d'appli : calcul pur
/// (<see cref="StageLayout.Shape"/>), vérifié par --self-test avec des fenêtres simulées.
/// </summary>
/// <param name="Process">Nom de l'exe sans « .exe » (null si illisible).</param>
/// <param name="OwnProcess">Fenêtre de CE processus d'Opaline.</param>
/// <param name="OwnedByVisible">Possédée (GW_OWNER, en remontant) par une fenêtre visible : dialogue, palette, fenêtre secondaire.</param>
/// <param name="Bounds">Partie visible (fenêtre affichée) ou place normale (fenêtre réduite), en pixels.</param>
/// <param name="OnScreen">Touche au moins un écran (sans objet pour une fenêtre réduite).</param>
internal readonly record struct StageWindowShape(string? Process, bool OwnProcess, string ClassName, int Style, int ExStyle,
    bool OwnedByVisible, bool Minimized, RECT Bounds, bool OnScreen, bool Cloaked, double Scale);

/// <summary>Comment mettre la fenêtre au centre.</summary>
internal enum StagePlaceMode
{
    /// <summary>Taille gardée (ou ramenée à 80 % si elle ne tient pas), centrée ; fenêtre non redimensionnable : seulement centrée.</summary>
    Center,
    /// <summary>Remplit la scène par SetWindowPos (colonne toujours visible : la scène n'est pas toute la zone de travail).</summary>
    Fill,
    /// <summary>Agrandissement natif de Windows (scène = toute la zone de travail) : aucune appli ne le refuse ni le « corrige ».</summary>
    Maximize,
}

/// <summary>Que faire de la fenêtre au premier plan (anti-clignotement, dialogues, nos propres mouvements).</summary>
internal enum StageForegroundDecision
{
    /// <summary>Pas encore : 300 ms pas écoulées, réorganisation en cours, fenêtre pas encore listée ou en train d'être rendue.</summary>
    Wait,
    /// <summary>Une autre fenêtre est passée au premier plan entre-temps : on recommence à compter pour elle.</summary>
    Restart,
    /// <summary>Rien à faire (dialogue / menu possédé, rien au premier plan, fenêtre jamais apparue).</summary>
    Ignore,
    /// <summary>Elle est restée au premier plan assez longtemps : elle prend le centre (si elle est gérée).</summary>
    Take,
}

/// <param name="Candidate">Fenêtre qu'on attend (la « racine » du premier plan au moment de l'événement).</param>
/// <param name="Since">Depuis quand elle est au premier plan.</param>
/// <param name="Foreground">Fenêtre au premier plan MAINTENANT.</param>
/// <param name="ForegroundRoot">Sa racine (on remonte les propriétaires visibles).</param>
/// <param name="Listed">La racine est une fenêtre d'appli suivie (WindowTracker).</param>
/// <param name="RootIconic">La racine est encore réduite (en train d'être rendue).</param>
/// <param name="Busy">Stage Manager est en train de réorganiser (nos SW_MINIMIZE / SW_RESTORE / SetWindowPos, échange animé).</param>
internal readonly record struct StageForegroundInput(IntPtr Candidate, DateTime Since, IntPtr Foreground, IntPtr ForegroundRoot,
    bool Listed, bool RootIconic, bool Busy, DateTime Now);

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

    /// <summary>
    /// La scène : la zone de travail moins la colonne à gauche (et une petite marge ailleurs) ; colonne auto-masquée
    /// (<paramref name="autoHide"/>) = toute la zone de travail (la colonne passe par-dessus quand on la fait venir).
    /// </summary>
    public static RECT StageArea(RECT work, double scale, bool autoHide = false)
    {
        int margin = (int)Math.Round(StageShape.ScreenMargin * scale);
        var stage = new RECT
        {
            Left = work.Left + (autoHide ? margin : ColumnReserve(scale)),
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

    /// <summary>Haut de la colonne (pixels) : centrée en hauteur dans la zone de travail, jamais au-dessus d'elle.</summary>
    public static int ColumnTop(RECT work, int heightPx) => Math.Max(work.Top, work.Top + (work.Bottom - work.Top - heightPx) / 2);

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
        : f.Invisible ? StageExclusion.Invisible
        : f.Popup ? StageExclusion.Popup
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

    // ------------------------------------------------------------------ vraies fenêtres d'appli (correctifs du 2026-11-01)

    /// <summary>Nom de l'exe d'Opaline : TOUT processus de ce nom (rendus --snapshot / --self-test d'un dossier de compilation…) est ignoré.</summary>
    public const string OwnExe = "DynamicIsland";

    /// <summary>En dessous (DIP), ce n'est pas une fenêtre d'appli (fenêtre de mesure, bouton flottant…).</summary>
    public const double MinWidth = 200, MinHeight = 150;
    /// <summary>Fenêtre NON redimensionnable plus petite que ça (DIP) : petit dialogue, fenêtre « À propos », écran de démarrage.</summary>
    public const double SmallFixedWidth = 520, SmallFixedHeight = 360;

    /// <summary>Une fenêtre ne prend le centre que si elle reste au premier plan au moins ce temps (anti-clignotement).</summary>
    public static readonly TimeSpan SettleTime = TimeSpan.FromMilliseconds(300);
    /// <summary>Fenêtre au premier plan jamais apparue dans la liste (ou toujours réduite) après ce temps : on laisse tomber.</summary>
    public static readonly TimeSpan GiveUpTime = TimeSpan.FromSeconds(2);
    /// <summary>Après un de NOS mouvements (réduire, rendre, déplacer) : les événements qu'il provoque sont ignorés pendant ce temps.</summary>
    public static readonly TimeSpan BusyTime = TimeSpan.FromMilliseconds(450);

    private const int WS_POPUP = unchecked((int)0x80000000);
    private const int WS_CAPTION = 0x00C00000;
    private const int WS_THICKFRAME = 0x00040000;
    private const int WS_EX_DLGMODALFRAME = 0x00000001;
    private const int WS_EX_TRANSPARENT = 0x00000020;

    public static bool IsOpaline(string? process, bool ownProcess) =>
        ownProcess || string.Equals(process, OwnExe, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Est-ce une vraie fenêtre d'appli ? <see cref="StageExclusion.Own"/> (n'importe quel processus d'Opaline),
    /// <see cref="StageExclusion.Invisible"/> (cloaked, transparente aux clics, hors de tout écran, minuscule) ou
    /// <see cref="StageExclusion.Popup"/> (dialogue #32770, possédée par une fenêtre visible, cadre de dialogue modal, popup sans barre
    /// de titre ni bord redimensionnable = menu / écran de démarrage / notification, petite fenêtre fixe), sinon None.
    /// Une fenêtre sans barre de titre MAIS redimensionnable (Discord, Spotify, Electron « frameless ») reste une vraie fenêtre.
    /// </summary>
    public static StageExclusion Shape(StageWindowShape w)
    {
        if (IsOpaline(w.Process, w.OwnProcess)) return StageExclusion.Own;
        if (w.Cloaked || (w.ExStyle & WS_EX_TRANSPARENT) != 0) return StageExclusion.Invisible;
        if (!w.Minimized && !w.OnScreen) return StageExclusion.Invisible;
        double scale = Math.Max(0.5, w.Scale);
        int width = w.Bounds.Right - w.Bounds.Left, height = w.Bounds.Bottom - w.Bounds.Top;
        if (width < MinWidth * scale || height < MinHeight * scale) return StageExclusion.Invisible;
        if (w.ClassName == "#32770" || w.OwnedByVisible || (w.ExStyle & WS_EX_DLGMODALFRAME) != 0) return StageExclusion.Popup;
        bool resizable = (w.Style & WS_THICKFRAME) != 0;
        if ((w.Style & WS_POPUP) != 0 && (w.Style & WS_CAPTION) != WS_CAPTION && !resizable) return StageExclusion.Popup;
        if (!resizable && (width < SmallFixedWidth * scale || height < SmallFixedHeight * scale)) return StageExclusion.Popup;
        return StageExclusion.None;
    }

    /// <summary>
    /// Anti-clignotement : la fenêtre au premier plan prend le centre seulement si elle y reste <see cref="SettleTime"/> (un passage
    /// éclair pendant un Alt+Tab ou une activation par une autre appli ne fait rien), jamais pendant une de nos réorganisations, et
    /// jamais pour un dialogue / menu possédé (la fenêtre du centre garde sa place).
    /// </summary>
    public static StageForegroundDecision Foreground(StageForegroundInput i)
    {
        if (i.Foreground == IntPtr.Zero || i.ForegroundRoot == IntPtr.Zero) return StageForegroundDecision.Ignore;
        if (i.Foreground != i.ForegroundRoot) return StageForegroundDecision.Ignore; // possédée par une autre fenêtre : on ne change rien
        if (i.ForegroundRoot != i.Candidate) return StageForegroundDecision.Restart;
        if (i.Busy) return StageForegroundDecision.Wait;
        var elapsed = i.Now - i.Since;
        if (elapsed < SettleTime) return StageForegroundDecision.Wait;
        if (!i.Listed || i.RootIconic) return elapsed < GiveUpTime ? StageForegroundDecision.Wait : StageForegroundDecision.Ignore;
        return StageForegroundDecision.Take;
    }

    /// <summary>Stage Manager est-il en train de réorganiser (événements provoqués par nous à ignorer) ?</summary>
    public static bool IsBusy(DateTime now, DateTime busyUntil, bool swapping) => swapping || now < busyUntil;

    /// <summary>
    /// Comment placer la fenêtre du centre. Non redimensionnable : seulement centrée. À remplir (« Applis en plein écran », ou déjà
    /// agrandie) avec la colonne auto-masquée (scène = toute la zone de travail) : agrandissement natif si elle a le bouton Agrandir
    /// (et si on peut l'activer, ShowWindow(SW_MAXIMIZE) l'active ; ou si elle l'est déjà) ; sinon SetWindowPos vers la scène.
    /// </summary>
    public static StagePlaceMode PlaceMode(bool fillStage, bool autoHide, bool resizable, bool maximizable, bool maximized, bool canActivate)
    {
        if (!resizable) return StagePlaceMode.Center;
        if (!fillStage && !maximized) return StagePlaceMode.Center;
        if (autoHide && maximizable && (maximized || canActivate)) return StagePlaceMode.Maximize;
        return StagePlaceMode.Fill;
    }

    /// <summary>Après un SetWindowPos « remplir » : l'appli a-t-elle changé sa taille (plus de 4 px) ? → une seule correction.</summary>
    public static bool NeedsResize(RECT target, RECT visible) =>
        Math.Abs((target.Right - target.Left) - (visible.Right - visible.Left)) > 4
        || Math.Abs((target.Bottom - target.Top) - (visible.Bottom - visible.Top)) > 4;

    /// <summary>Rectangle de la fenêtre entière pour une partie visible voulue (bords invisibles de Windows 11 ajoutés).</summary>
    public static RECT WithBorders(RECT visible, RECT windowRect, RECT visibleRect) => new()
    {
        Left = visible.Left - (visibleRect.Left - windowRect.Left),
        Top = visible.Top - (visibleRect.Top - windowRect.Top),
        Right = visible.Right + (windowRect.Right - visibleRect.Right),
        Bottom = visible.Bottom + (windowRect.Bottom - visibleRect.Bottom),
    };
}
