using System.IO;
using DynamicIsland.Dock;
using DynamicIsland.Settings;
using DynamicIsland.Themes;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des correctifs 15 et 16 (priorité 2) : alignement et grossissement du dock, petites tailles, icônes du bureau.
/// Calculs purs et lecture seule : rien n'est caché, montré ni écrit (ni dans Windows, ni dans les réglages).
/// </summary>
internal static partial class SelfTest
{
    private static void Dock2(List<string> lines)
    {
        Dock2Magnification(lines);
        Dock2Sizes(lines);
        Dock2DesktopIcons(lines);
    }

    /// <summary>15a : aligné à gauche / à droite, la plus grosse icône est sous la souris ; centré = exactement le calcul d'avant.</summary>
    private static void Dock2Magnification(List<string> lines)
    {
        const double icon = 40, left = 300;
        double width = icon + 2 * icon * DockShape.GapRatio;
        var widths = new List<double>();
        var icons = new List<bool>();
        for (int i = 0; i < 9; i++)
        {
            if (i == 5)
            {
                widths.Add(Stroke.Hairline + 2 * Space.S3); // séparateur épinglées | ouvertes : ne grossit pas
                icons.Add(false);
            }
            widths.Add(width);
            icons.Add(true);
        }
        double content = widths.Sum();

        foreach (var alignment in new[] { DockAlignment.Left, DockAlignment.Right })
        {
            int tested = 0, wrong = 0;
            double worstGap = 0;
            foreach (var magnification in new[] { 1.2, 1.4, 1.8 })
            {
                for (double pointer = left + 2; pointer < left + content - 2; pointer += 3)
                {
                    var targets = DockWindow.MagnifyTargets(widths, icons, left, pointer, alignment, width, icon, magnification);
                    // Disposition une fois grossie (le bord gauche, ou droit, ne bouge pas) : chaque icône s'élargit.
                    var growth = widths.Select((w, i) => icons[i] ? (targets[i] - 1) * icon * w / width : 0).ToList();
                    double x = alignment == DockAlignment.Right ? left - growth.Sum() : left;
                    int under = -1;
                    for (int i = 0; i < widths.Count; i++)
                    {
                        double next = x + widths[i] + growth[i];
                        if (pointer >= x && pointer < next) under = i;
                        x = next;
                    }
                    if (under < 0 || !icons[under]) continue; // souris sur le séparateur
                    tested++;
                    if (targets[under] < targets.Max() - 1e-9) wrong++;
                    // Sans la correction (centre du grossissement = la souris, comme pour un dock centré) : de combien le point
                    // survolé aurait été poussé par les icônes grossies avant lui.
                    var before = DockWindow.MagnifyTargets(widths, icons, left, pointer, DockAlignment.Center, width, icon, magnification);
                    worstGap = Math.Max(worstGap, Math.Abs(DockWindow.MagnifiedPosition(pointer, before, widths, icons, left, alignment, width, icon) - pointer));
                }
            }
            Check(lines, tested > 200 && wrong == 0,
                $"grossissement aligné {(alignment == DockAlignment.Left ? "à gauche" : "à droite")} : l'icône sous la souris est la plus grosse "
                + $"({tested} positions, ×1,2 à ×1,8 ; l'ancien calcul l'aurait décalée jusqu'à {worstGap:0} px)");
        }

        // Centré : exactement l'ancien calcul (distances prises au repos, centre = la souris).
        bool same = true;
        for (double pointer = left; pointer < left + content; pointer += 7)
        {
            var targets = DockWindow.MagnifyTargets(widths, icons, left, pointer, DockAlignment.Center, width, icon, 1.4);
            double x = left;
            for (int i = 0; i < widths.Count; i++)
            {
                double distance = Math.Abs(pointer - (x + widths[i] / 2)) / width, old = 1;
                if (icons[i] && distance < DockShape.MagnificationRange)
                    old = 1 + 0.4 * (Math.Cos(Math.PI * distance / DockShape.MagnificationRange) + 1) / 2;
                same &= Math.Abs(old - targets[i]) < 1e-12;
                x += widths[i];
            }
        }
        var none = DockWindow.MagnifyTargets(widths, icons, left, null, DockAlignment.Left, width, icon, 1.4);
        var far = DockWindow.MagnifyTargets(widths, icons, left, left + content + 400, DockAlignment.Left, width, icon, 1.4);
        Check(lines, same && none.All(t => t == 1) && far.All(t => t == 1),
            "grossissement centré identique à avant ; rien ne grossit sans souris ni loin des applis");

        Check(lines, DockWindow.SnapSide(-495, 500) == DockAlignment.Left && DockWindow.SnapSide(490, 500) == DockAlignment.Right
                     && DockWindow.SnapSide(-300, 500) == DockAlignment.Center && DockWindow.SnapSide(0, 500) == DockAlignment.Center
                     && DockWindow.SnapSide(-10, 12) == DockAlignment.Center,
            "dock flottant lâché contre un côté → aligné sur ce côté ; ailleurs centré avec son décalage");
    }

    /// <summary>15b : de 24 à 72 px, tout tient dans le fond ; à 40 et 72 px, mêmes tailles qu'avant.</summary>
    private static void Dock2Sizes(List<string> lines)
    {
        var overflow = new List<string>();
        foreach (bool bar in new[] { true, false })
        {
            for (double icon = DockShape.MinIcon; icon <= DockShape.MaxIcon; icon += 2)
            {
                var m = DockWindow.MetricsFor(icon, bar);
                foreach (var (name, height) in new[] { ("recherche", m.Search), ("profil", m.Profile), ("zone d'état", m.StatusCell), ("icônes d'arrière-plan", m.TrayRows * m.TrayCell) })
                {
                    double offset = DockWindow.EdgeOffset(m.IconCenter, height, m.Background);
                    if (offset < 0 || offset + height > m.Background + 0.01) overflow.Add($"{name} à {icon} px ({(bar ? "barre" : "flottant")})");
                }
                if (m.TrayCell < DockShape.TrayIcon) overflow.Add($"cases des icônes d'arrière-plan < 16 px à {icon} px");
            }
        }
        var small = DockWindow.MetricsFor(DockShape.MinIcon, true);
        Check(lines, overflow.Count == 0 && small.Background is > 30 and < 33 && small.TrayRows == 1,
            $"de 24 à 72 px, tout tient dans le fond (barre de {small.Background:0.0} px à 24 px, icônes d'arrière-plan sur {small.TrayRows} ligne de {small.TrayCell} px)"
            + (overflow.Count > 0 ? " — dépasse : " + string.Join(", ", overflow) : ""));

        bool unchanged = true;
        foreach (double icon in new[] { 40.0, 72.0 })
        {
            double scale = icon / DockShape.ReferenceIcon;
            var m = DockWindow.MetricsFor(icon, true);
            unchanged &= m.Search == Math.Max(30, DockShape.SearchHeight * scale)
                         && m.Profile == Math.Round(Math.Max(28, DockShape.Profile * scale))
                         && m.StatusCell == Math.Round(Math.Max(28, DockShape.StatusCell * scale))
                         && m.TrayRows == 2 && m.TrayCell == DockShape.TrayCell
                         && DockWindow.EdgeOffset(m.IconCenter, 2 * DockShape.TrayCell, m.Background) == Math.Max(0, m.IconCenter - DockShape.TrayCell);
        }
        Check(lines, unchanged, "à 40 et 72 px : recherche, profil, zone d'état et icônes d'arrière-plan comme avant");

        Check(lines, TrayArea.LayoutFor(52.2) == (2, 22) && TrayArea.LayoutFor(36.5) == (2, 18) && TrayArea.LayoutFor(31.3) == (1, 22)
                     && TrayArea.LayoutFor(10) == (1, DockShape.TrayCellMin),
            "icônes d'arrière-plan : 2 lignes tant que des cases de 18 tiennent, sinon une seule (icônes toujours à 16 px)");

        var look = new AppearanceSettings();
        bool bounds = true;
        foreach (var (value, expected) in new[] { (10.0, 24.0), (100.0, 72.0), (24.0, 24.0), (23.6, 24.0), (40.0, 40.0), (double.NaN, 40.0) })
        {
            look.DockIconSize = value;
            look.Normalize();
            bounds &= look.DockIconSize == expected;
        }
        var dock = new DockSettings { Alignment = (DockAlignment)7, CloseSize = (PreviewCloseSize)9, CloseColor = (PreviewCloseColor)5 };
        dock.Normalize();
        Check(lines, bounds && dock.Alignment == DockAlignment.Center && dock.CloseSize == PreviewCloseSize.Medium && dock.CloseColor == PreviewCloseColor.Gray
                     && !new DockSettings().HideDesktopIcons,
            "réglages : taille des icônes de 24 à 72 px, valeurs inconnues → centré / croix moyenne et grise, icônes du bureau affichées par défaut");
    }

    /// <summary>16 : décisions pures + recherche de la vue des icônes en LECTURE seule (rien n'est caché ni montré).</summary>
    private static void Dock2DesktopIcons(List<string> lines)
    {
        Check(lines, DesktopIconsService.ShouldHide(true, true, true) && !DesktopIconsService.ShouldHide(false, true, true)
                     && !DesktopIconsService.ShouldHide(true, false, false) && !DesktopIconsService.ShouldHide(true, true, false),
            "icônes du bureau : cachées seulement si l'option est active et la vue trouvée et visible (« Afficher les icônes » de Windows respecté)");
        Check(lines, DesktopIconsService.RestorePlan(false, true, false, false) == (false, false)
                     && DesktopIconsService.RestorePlan(true, false, false, false) == (false, false)
                     && DesktopIconsService.RestorePlan(true, true, false, false) == (true, true)
                     && DesktopIconsService.RestorePlan(true, true, true, false) == (false, true)
                     && DesktopIconsService.RestorePlan(true, true, false, true) == (false, true),
            "remise d'après la trace : rien sans trace, trace gardée sans Explorateur, remises sinon (déjà revenues ou cachées par Windows : trace oubliée)");
        Check(lines, Path.GetDirectoryName(AppPaths.DesktopIconsState) == AppPaths.DataFolder
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Services.DesktopIconsService.Apply()") == ModuleGuard.Status,
            "trace dans %APPDATA%\\DynamicIsland ; une erreur du service arrête le module « zone d'état » (icônes remises)");

        var defaults = StatusItem.Defaults();
        var old = StatusItem.NormalizeList([new() { Id = StatusItem.Keyboard }, new() { Id = StatusItem.Updates }, new() { Id = StatusItem.Trash }, new() { Id = StatusItem.Downloads }]);
        Check(lines, defaults.First(i => i.Id == StatusItem.Desktop) is { Visible: false } && old[4] is { Id: StatusItem.Desktop, Visible: false } && old.Take(4).All(i => i.Visible),
            "zone d'état : la flèche des icônes du bureau arrive à la fin, cachée tant qu'on ne la demande pas");

        var (view, host) = DesktopIconsService.Locate();
        bool visible = view != IntPtr.Zero && Interop.DockNative.IsWindowVisible(view);
        Check(lines, true, view == IntPtr.Zero
            ? "vue des icônes du bureau introuvable (Explorateur absent ?) : rien à cacher"
            : $"vue des icônes du bureau trouvée en lecture seule (SysListView32 sous SHELLDLL_DefView, dans {host}), visible : {(visible ? "oui" : "non")}"
              + $", trace présente : {(File.Exists(AppPaths.DesktopIconsState) ? "oui" : "non")}");
    }
}
