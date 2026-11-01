using System.Windows;
using DynamicIsland.Animation;
using DynamicIsland.Settings;
using DynamicIsland.Themes;
using DynamicIsland.Tiles;

namespace DynamicIsland;

/// <summary>
/// Grille de tuiles (refonte O6, étape 1) : l'île ouverte montre une grille « bento » (accueil) à la place de la rangée d'onglets ;
/// un clic sur une tuile l'agrandit (MorphOpen) en « outil » = l'onglet existant, avec « ← Nom » en haut à gauche pour revenir.
/// Tout ce qui ouvrait un onglet (palette, dock, annonces, Claude…) ouvre directement l'outil (SelectTab). À la fermeture de l'île,
/// on revient à la grille pour la prochaine ouverture. Réglage <see cref="IslandSettings.Layout"/> = Onglets : l'ancien système,
/// inchangé (<see cref="_home"/> reste faux).
/// </summary>
public partial class IslandWindow
{
    /// <summary>Mode grille : la grille est affichée (sinon un outil = l'onglet <see cref="_tab"/>). Toujours faux en mode Onglets.</summary>
    private bool _home;
    /// <summary>L'île s'est refermée sur un outil : à la prochaine ouverture, retour à la grille.</summary>
    private bool _returnHome;
    /// <summary>L'outil ouvert (<see cref="_morph"/>) est parti d'une tuile de la grille.</summary>
    private bool _morphFromHome;
    /// <summary>Tuile d'où l'outil est parti (la tuile Moniteur ouvre l'outil Santé : le retour rentre dans le Moniteur).</summary>
    private IslandLayout? _layoutShown;
    private TileContext? _tileContext;
    private string _gridDate = "";

    private static bool GridLayout => SettingsStore.Current.Island.Layout == IslandLayout.Grid;

    /// <summary>Onglets en colonne : seulement en mode Onglets.</summary>
    private static bool VerticalTabs => SettingsStore.Current.Island.VerticalTabs && !GridLayout;

    private TileContext TileContext => _tileContext ??= new TileContext
    {
        Media = _media,
        Timer = _timer,
        ToolAvailable = id => TabIds.FirstOrDefault(t => t.Id == id) is { Id: not null } entry && TabHasContent(entry.Tab) && !TabStopped(entry.Tab),
        ExistingView = id => TabIds.FirstOrDefault(t => t.Id == id) is { Id: not null } entry ? ViewOf(entry.Tab) : null,
        MeterLevel = () => _volume.Peak(), // même vumètre que l'égaliseur de l'île réduite
        OpenPalette = text =>
        {
            if (!Services.ModuleGuard.IsDisabled(Services.ModuleGuard.Palette)) Palette.PaletteWindow.Toggle(this, text); // la saisie sort de l'île
        },
        CollapseIsland = () =>
        {
            _expandDelay.Stop();
            Collapse();
        },
        Notice = (glyph, text, click) => ShowNotice(glyph, text, click),
    };

    /// <summary>Tuile d'où l'outil ouvert est parti (l'horloge analogique ouvre l'outil Horloges : il rentre dans SA tuile).</summary>
    private (TileFace Face, Tab Tab)? _openedFrom;

    private void InitializeGrid()
    {
        TileGrid.TileClicked += OpenTile;
        TileGrid.TileRightClicked += face =>
        {
            if (face.Definition.RightClick is { } action) RunTileAction(face, action);
        };
    }

    /// <summary>Action d'une tuile (clic, clic droit) : une erreur arrête le module de la tuile (cachée pour la session), jamais l'île.</summary>
    private void RunTileAction(TileFace face, Action<TileContext, TileFace> action)
    {
        var definition = face.Definition;
        try
        {
            action(TileContext, face);
            face.Refresh(); // l'état change tout de suite (micro coupé, enregistrement…)
        }
        catch (Exception ex)
        {
            if (definition.Module is { } module) Services.ModuleGuard.Fail(module, ex);
            else Log.Error($"Tuile « {definition.Id} » : action", ex);
            RefreshGrid();
        }
    }

    /// <summary>Grille ou onglets (réglage) : l'en-tête et la vue affichée suivent.</summary>
    private void ApplyIslandLayout()
    {
        var layout = SettingsStore.Current.Island.Layout;
        if (_layoutShown == layout) return;
        bool first = _layoutShown == null;
        _layoutShown = layout;
        bool grid = layout == IslandLayout.Grid;
        TabScroller.Visibility = grid ? Visibility.Collapsed : Visibility.Visible;
        GridHeader.Visibility = grid ? Visibility.Visible : Visibility.Collapsed;
        _verticalShown = null; // la colonne d'onglets n'existe qu'en mode Onglets : on la remet en place
        ApplyTabLayout();
        if (grid)
        {
            GoHome(animate: false);
        }
        else
        {
            _home = false;
            _returnHome = false;
            if (!first) SelectTab(_tab, animate: false);
        }
    }

    /// <summary>Tuiles affichées : celles de la disposition enregistrée qui ont quelque chose à montrer, placées en pages.</summary>
    private void RefreshGrid()
    {
        var context = TileContext;
        var grid = SettingsStore.Current.Island.EnsureGrid();
        var slots = TileLayout.Place(grid.Ordered(id => TileRegistry.Find(id) is { } definition && TileShown(definition, context)));
        TileGrid.Show(slots, (definition, size) => definition.Create(context, size));
        if (_home && _expanded) AnimateTo(ExpandedSize(_tab), ExpandedRadius, Springs.Resize);
    }

    private static bool TileShown(TileDefinition definition, TileContext context)
    {
        try
        {
            return (definition.Module == null || !Services.ModuleGuard.IsDisabled(definition.Module)) && definition.IsAvailable(context);
        }
        catch (Exception ex)
        {
            Log.Error($"Tuile « {definition.Id} » : condition d'affichage", ex);
            return false;
        }
    }

    /// <summary>Retour à la grille (« ← Nom », fermeture puis réouverture) : l'outil rentre dans sa tuile s'il en était sorti.</summary>
    private void GoHome(bool animate)
    {
        if (!GridLayout) return;
        bool wasHome = _home;
        _home = true;
        _returnHome = false;
        var morph = _morph;
        _morph = null;
        // L'outil rentre dans la tuile qui l'a ouvert si c'est toujours lui qui est affiché (sinon : la tuile de cet outil).
        var origin = _openedFrom is { } opened && opened.Tab == _tab && TileGrid.Faces.Contains(opened.Face) ? opened.Face : TileGrid.FaceOf(IdOf(_tab));
        _openedFrom = null;
        var face = morph is { IsOpen: true } && _morphFromHome && origin is { } f && TileGrid.IsOnCurrentPage(f) ? f : null;
        if (animate && _expanded && !wasHome && morph != null && face != null)
        {
            morph.Close(() => ShowPages(animate: true), face);
        }
        else
        {
            morph?.Finish();
            ShowPages(animate);
        }
        _morphFromHome = false;
        UpdateGridHeader();
        SyncActive();
        if (_expanded) AnimateTo(ExpandedSize(_tab), ExpandedRadius, Springs.Resize);
    }

    private void BackButton_Click(object sender, RoutedEventArgs e) => GoHome(animate: true);

    /// <summary>Clic sur une tuile : sa vue détaillée s'ouvre en grand (la tuile s'agrandit jusqu'à remplir l'île).</summary>
    private void OpenTile(TileFace face)
    {
        if (!_expanded || !_home) return;
        var definition = face.Definition;
        if (definition.Click is { } click)
        {
            RunTileAction(face, click); // tuile-outil : son action, pas de vue détaillée
            return;
        }
        FrameworkElement? detail;
        try
        {
            detail = definition.CreateDetail(TileContext);
        }
        catch (Exception ex)
        {
            Log.Error($"Tuile « {definition.Id} » : vue détaillée", ex);
            return;
        }
        // Étape 1 : chaque vue détaillée est un onglet existant. Une vue nouvelle (O8) sera hébergée dans TabPages avec DetailSize.
        if (detail == null || TabOfView(detail) is not Tab tab)
        {
            Log.Info($"Tuile « {definition.Id} » : pas de vue détaillée à ouvrir");
            return;
        }
        if (!TileContext.ToolAvailable(IdOf(tab))) return;
        _morph?.Finish();
        _morph = MorphOpen.Open(face, TabPages, Corners.XL, () =>
        {
            SelectTab(tab, animate: true, slide: false);
            Cascade.Play(ViewOf(tab), reverse: false); // le contenu de l'outil arrive sous la tuile qui s'agrandit
        });
        _morphFromHome = true;
        _openedFrom = (face, tab);
        _morphTo = tab;
    }

    private Tab? TabOfView(FrameworkElement view)
    {
        foreach (var (tab, _) in TabIds)
            if (ReferenceEquals(ViewOf(tab), view)) return tab;
        return null;
    }

    /// <summary>Vue affichée sous l'en-tête : la page de la grille, ou l'outil / l'onglet.</summary>
    private FrameworkElement CurrentView => _home ? TileGrid.CurrentPageElement : ViewOf(_tab);

    /// <summary>Fondus des vues : seule celle qui doit se voir reste (grille ou onglet <see cref="_tab"/>), cliquable.</summary>
    private void ShowPages(bool animate)
    {
        foreach (var (tab, _) in TabIds) ShowPage(ViewOf(tab), !_home && tab == _tab, animate);
        ShowPage(TileGrid, _home, animate);
        if (!animate) TileGrid.FinishAnimations();
    }

    private void ShowPage(FrameworkElement view, bool visible, bool animate)
    {
        view.IsHitTestVisible = visible;
        if (animate)
        {
            Fade(view, visible ? 1 : 0, visible ? Motion.Medium : Motion.Fast, visible ? Motion.Delay : TimeSpan.Zero);
        }
        else
        {
            view.BeginAnimation(OpacityProperty, null);
            _fadeTargets.Remove(view);
            view.Opacity = visible ? 1 : 0;
            PageTransition.Stop(view);
        }
    }

    /// <summary>En-tête du mode grille : la date sur la grille, « ← Nom de l'outil » dans un outil.</summary>
    private void UpdateGridHeader()
    {
        if (!GridLayout) return;
        BackButton.Visibility = _home ? Visibility.Collapsed : Visibility.Visible;
        GridDate.Visibility = _home ? Visibility.Visible : Visibility.Collapsed;
        if (!_home) BackTitle.Text = TileRegistry.Find(IdOf(_tab))?.Name ?? "";
        UpdateGridDate();
    }

    /// <summary>« samedi 1 novembre » (relu à chaque ouverture : la date change à minuit).</summary>
    private void UpdateGridDate()
    {
        string text = DateTime.Now.ToString(Loc.T("tiles.date_format"), Loc.Culture);
        if (text.Length > 0) text = char.ToUpper(text[0], Loc.Culture) + text[1..];
        if (text == _gridDate) return;
        _gridDate = text;
        GridDate.Text = text;
    }

    /// <summary>Taille de l'île ouverte en mode grille : la grille, ou l'outil (élargi si « ← Nom » + l'heure ne tiennent pas).</summary>
    private Size GridExpandedSize(Tab tab)
    {
        if (_home) return TileGridShape.Island;
        var size = SizeFor(tab);
        var any = new Size(double.PositiveInfinity, double.PositiveInfinity);
        BackButton.Measure(any);
        HeaderRight.Measure(any);
        double needed = BackButton.DesiredSize.Width + HeaderRight.DesiredSize.Width + 2 * Space.S8 + Space.S5 + Space.S4;
        return new Size(Math.Min(IslandShape.MaxExpandedWidth, Math.Max(size.Width, needed)), size.Height);
    }

    /// <summary>
    /// Ouvre l'outil <paramref name="tab"/> (palette, dock, annonce…) : en mode grille, même si l'onglet était déjà le dernier
    /// ouvert (la grille est peut-être affichée), et sans retour à la grille à l'ouverture.
    /// </summary>
    private void ShowTool(Tab tab)
    {
        if (_tab != tab || _home) SelectTab(tab, animate: _expanded);
        _returnHome = false;
    }

    /// <summary>
    /// --snapshot (mode grille) : grille page 1, page 2, nom d'une tuile survolée (inclinée), outil ouvert avec « ← Nom », et une
    /// image figée au milieu du morphing tuile → outil. Données d'exemple déjà posées par <see cref="RunSnapshotAsync"/>.
    /// </summary>
    private async Task SnapshotGridAsync(string folder)
    {
        if (!GridLayout) return;
        UpdateFriendsTab(); // amis et horloges d'exemple : leurs tuiles prennent place
        Collapse();
        FinishAnimations();
        GoHome(animate: false);
        TileGrid.ShowPage(0, animate: false);
        Expand();
        TileGrid.RefreshAll();
        FinishAnimations();
        _opal.HideSweep();
        await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, "4a-grille-page1.png"));

        if (TileGrid.PageCount > 1)
        {
            // Toutes les pages de la disposition par défaut (une par thème) : 4b-grille-page2.png, 4b-grille-page3.png…
            for (int page = 1; page < TileGrid.PageCount; page++)
            {
                TileGrid.ShowPage(page, animate: false);
                TileGrid.RefreshAll();
                FinishAnimations();
                TileGrid.FinishAnimations();
                await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, $"4b-grille-page{page + 1}.png"));
            }
            TileGrid.ShowPage(0, animate: false);
            FinishAnimations();
        }

        // Survol : la tuile Contrôles penche vers la souris, son nom apparaît en bas au centre.
        if (TileGrid.FaceOf(IslandTabItem.Control) is { } hovered && TileGrid.IsOnCurrentPage(hovered))
        {
            TileMotion.ShowPose(hovered, new Point(hovered.ActualWidth * 0.8, hovered.ActualHeight * 0.3), pressed: false);
            Design.OpalTile.ShowGlow(hovered, true);
            TileGrid.ShowHoverName(hovered);
            await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, "4c-grille-survol.png"));
            TileMotion.ClearPose(hovered);
            Design.OpalTile.ShowGlow(hovered, false);
            TileGrid.ShowHoverName(null);
            FinishAnimations();
        }

        // Clic sur Musique : image figée au milieu du morphing (la tuile grandit vers l'outil), puis l'outil posé.
        if (TileGrid.FaceOf(IslandTabItem.Music) is { } music && TileGrid.IsOnCurrentPage(music))
        {
            OpenTile(music);
            if (_animating)
            {
                StopRendering();
                for (int i = 0; i < 7; i++) StepGeometry(1 / 60.0);
                ApplyGeometry();
            }
            MotionClock.Advance(0.12);
            MotionClock.Frozen = true;
            // Fondus WPF à 0,12 s (ils ne suivent pas l'horloge des mouvements) : la grille est partie, l'outil arrive.
            foreach (var (view, opacity) in new[] { ((FrameworkElement)TileGrid, 0.0), (ViewOf(Tab.Music), 0.45) })
            {
                view.BeginAnimation(OpacityProperty, null);
                view.Opacity = opacity;
            }
            await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, "4e-grille-morphing.png"));
            MotionClock.Frozen = false;
            FinishAnimations();
            ShowPages(animate: false); // fondus terminés
            await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, "4d-outil-musique.png"));
        }
        GoHome(animate: false);
        FinishAnimations();
        await SnapshotEverydayTilesAsync(folder); // tuiles de tous les jours (lot A) : 4h-tous-les-jours-*.png
        await SnapshotLiveTilesBAsync(folder);
    }

    /// <summary>
    /// --snapshot : tuiles vivantes du lot B dans toutes leurs tailles, avec des données d'exemple (mesures inventées, amis
    /// d'exemple ; profils = ceux des réglages en lecture seule) : 3 pages posées à la main, puis la vraie grille revient.
    /// </summary>
    private async Task SnapshotLiveTilesBAsync(string folder)
    {
        TileSamples.Enabled = true;
        TileSamples.Friends = Settings.SettingsWindow.SampleFriends.Friends;
        try
        {
            var context = TileContext;
            var slots = TileLayout.Place([
                [(LiveTilesB.MonitorId, TileSize.L), (IslandTabItem.Friends, TileSize.W), (LiveTilesB.ProfileId, TileSize.W)],
                [(IslandTabItem.Friends, TileSize.L), (LiveTilesB.MonitorId, TileSize.W), (LiveTilesB.ProfileId, TileSize.S), (LiveTilesB.MonitorId, TileSize.S)],
                [(IslandTabItem.Friends, TileSize.S)],
            ]);
            Expand();
            TileGrid.Show(slots, (definition, size) => definition.Create(context, size));
            for (int page = 0; page < TileGrid.PageCount; page++)
            {
                TileGrid.ShowPage(page, animate: false);
                TileGrid.RefreshAll();
                FinishAnimations();
                TileGrid.FinishAnimations();
                _opal.HideSweep();
                await SnapshotRenderer.SaveAsync(this, System.IO.Path.Combine(folder, $"4g-vivantes-{page + 1}.png"));
            }
        }
        finally
        {
            TileSamples.Enabled = false;
            TileSamples.Friends = null;
            TileGrid.ShowPage(0, animate: false);
            RefreshGrid(); // la vraie disposition revient
            FinishAnimations();
        }
    }

    /// <summary>--snapshot : plus de ressort piloté par l'image (le rendu avance les ressorts à la main).</summary>
    private void StopRendering()
    {
        System.Windows.Media.CompositionTarget.Rendering -= OnRendering;
        _animating = false;
    }

    /// <summary>--self-test : la tuile <paramref name="id"/> ouvre bien la vue de son onglet (sans rien afficher).</summary>
    internal bool TileOpensItsView(string id) =>
        TileRegistry.Find(id) is { } definition && definition.CreateDetail(TileContext) is { } view
        && TabIds.FirstOrDefault(t => t.Id == id) is { Id: not null } entry && ReferenceEquals(view, ViewOf(entry.Tab));
}
