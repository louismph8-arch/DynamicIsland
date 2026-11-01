using System.IO;
using System.Windows;
using System.Windows.Media;
using DynamicIsland.Settings;

namespace DynamicIsland;

/// <summary>
/// Mode développeur : <c>DynamicIsland.exe --snapshot &lt;dossier&gt;</c> rend l'île (compacte puis
/// chaque onglet) dans des PNG, hors écran, puis quitte. Sert à vérifier le rendu sans capture d'écran.
/// </summary>
public partial class IslandWindow
{
    public string? SnapshotFolder { get; init; }

    /// <summary>--glass-bench : fenêtre hors écran comme pour --snapshot, mais sans les rendus.</summary>
    public bool GlassBenchOnly { get; init; }
    internal Controls.LiquidGlass BenchGlass => _glass;
    internal FrameworkElement BenchGlassSurface => Island;

    /// <summary>--perf-bench (7A) : effet de saison montré et animé (ou figé), comme dans l'île réelle.</summary>
    internal void BenchSeason(Services.Seasons.Season season, bool run)
    {
        SeasonLayer.Show(season);
        SeasonLayer.Pause(!run);
    }

    /// <summary>--perf-bench : comme une île restée fermée un moment (les particules se figent).</summary>
    internal void BenchSeasonSettle() => SeasonLayer.Settle();

    internal void BenchExpand(bool open)
    {
        if (open) Expand();
        else Collapse();
        FinishAnimations();
    }

    internal void BenchGaming(bool on)
    {
        ApplyGaming(on);
        FinishAnimations();
    }

    /// <summary>--glass-bench : île ouverte sur l'onglet Musique, animations terminées.</summary>
    internal void PrepareGlassBench()
    {
        Expand();
        SelectTab(Tab.Music, animate: false);
        FinishAnimations();
    }

    private async Task RunSnapshotAsync(string folder)
    {
        Directory.CreateDirectory(folder);
        if (App.SnapshotDemo) _media.UseDemo(); // images publiques : musique inventée
        // Étagère d'exemple (jamais sauvegardée) : quelques fichiers qui existent sur tous les PC.
        var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        Services.ShelfService.UseTemporaryItems([
            Path.Combine(windows, "Web", "Wallpaper", "Windows", "img0.jpg"),
            Path.Combine(windows, "win.ini"),
            Path.Combine(windows, "notepad.exe"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
        ]);
        // Zone « Téléphone » de l'étagère (7E) : réglages en lecture seule, on l'active en mémoire avec un fichier qui attend.
        var remote = SettingsStore.Current.Remote;
        remote.Enabled = remote.FileTransfer = true;
        if (remote.Devices.Count == 0) remote.Devices.Add(new RemoteDevice { Id = "demo", Name = "iPhone" });
        Services.Remote.RemoteOutbox.Add([Path.Combine(windows, "win.ini")]);
        await Task.Delay(1500); // laisse les services (média, écrans) répondre
        await Design.OpalBackdrop.WhenReady(); // copie floutée (fond du pack) derrière le verre opalin

        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "0-compacte.png"));

        // Salon vocal d'exemple (réglages en lecture seule : on peut activer Discord en mémoire).
        Settings.SettingsStore.Current.Discord.Enabled = true;
        Services.DiscordVoice.UseDemo();
        await Task.Delay(1500); // avatars (par défaut) téléchargés depuis Discord
        UpdateCompact();
        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "0c-vocal.png"));

        ApplyGaming(true);
        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "0b-gaming.png"));
        ApplyGaming(false);

        ShowSampleClocks();
        WidgetsTab.UseSampleData = true; // widgets des potes d'exemple en bas de l'onglet Widgets (7H) : rien n'est mesuré ni demandé
        FriendsTabButton.Visibility = Visibility.Visible; // amis d'exemple (rien n'est envoyé)
        FriendsTab.ShowSample(Settings.SettingsWindow.SampleFriends.Friends);
        HealthTab.ShowSample();
        MixerTab.ShowSample(); // applis d'exemple : aucun volume n'est lu ni réglé
        ControlTab.ShowDevices( // appareils d'exemple (correctif urgent 8)
        [
            new(Services.DeviceListService.Kind.Headset, "PRO X 2 LIGHTSPEED", 80, null),
            new(Services.DeviceListService.Kind.Controller, "Xbox Wireless Controller", 15, null),
            new(Services.DeviceListService.Kind.Mouse, "PRO Wireless", null, null),
            new(Services.DeviceListService.Kind.Storage, "Clé USB (E:)", null, "E:\\"),
        ]);
        foreach (var (tab, name) in new[] { (Tab.Music, "1-musique"), (Tab.Calendar, "2-calendrier"), (Tab.Control, "3-controles"), (Tab.Widgets, "3b-widgets"), (Tab.Shelf, "3c-etagere"), (Tab.Voice, "3d-vocal"), (Tab.Claude, "3e-claude"), (Tab.Clocks, "3f-horloges"), (Tab.Friends, "3g-amis"), (Tab.Health, "3h-sante"), (Tab.Mixer, "3i-melangeur"), (Tab.Clipboard, "3i-presse-papiers"), (Tab.Notes, "3m-notes") })
        {
            Expand();
            SelectTab(tab, animate: false);
            if (tab == Tab.Widgets) await Task.Delay(2500); // météo (réseau) + 2 mesures pour les pourcentages
            if (tab == Tab.Claude) ClaudeTab.ShowSample(); // conversation d'exemple : rien n'est demandé à Claude
            if (tab == Tab.Clipboard) ClipboardTab.ShowSample(vault: false); // historique d'exemple : le vrai presse-papiers n'est pas lu
            if (tab == Tab.Notes) NotesTab.ShowSample(); // notes d'exemple : notes.json n'est jamais lu
            FinishAnimations();
            _opal.HideSweep(); // le balayage de l'ouverture ne tourne pas dans un rendu figé
            await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, name + ".png"));
            if (tab == Tab.Clipboard)
            {
                ClipboardTab.ShowSample(vault: true); // coffre d'exemple : le vrai coffre n'est jamais ouvert
                FinishAnimations();
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "3i-coffre.png"));
            }
            if (tab == Tab.Control)
            {
                // Concentration (7F) : Ne pas déranger et veilleuse allumés pour l'exemple (rien n'est changé dans Windows).
                ControlTab.ShowFocusSample();
                FocusMoon.Visibility = ExpandedFocusMoon.Visibility = Visibility.Visible;
                FinishAnimations();
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "3-controles-concentration.png"));
                Collapse();
                FinishAnimations();
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "0c-ne-pas-deranger.png"));
                ControlTab.RefreshFocus();
                ControlTab.RefreshNightLight();
                UpdateFocusIndicator();
            }
            if (tab == Tab.Music)
            {
                // Verre liquide : lumière (curseur simulé) en haut à droite de l'île.
                _glass.SimulateCursor(new Point(Island.ActualWidth * 0.75, Island.ActualHeight * 0.2));
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "1b-musique-reflet.png"));
                _glass.SimulateCursor(null);
                // Verre opalin (O1) : balayage nacré figé au milieu de l'île, comme à l'ouverture.
                _opal.ShowSweepAt(0.45);
                await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "1c-musique-balayage.png"));
                _opal.HideSweep();
                // Thèmes saisonniers (6F) : l'effet avancé de quelques secondes, sans animation en direct.
                foreach (var (season, seasonName) in new[] { (Services.Seasons.Season.Halloween, "3j-halloween"), (Services.Seasons.Season.Christmas, "3k-noel") })
                {
                    SeasonLayer.Show(season);
                    SeasonLayer.Pause(true);
                    SeasonLayer.Prime(3);
                    await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, seasonName + ".png"));
                }
                SeasonLayer.Show(Services.Seasons.Season.None);
            }
        }

        // Minuteur (correctif 19) : éditeur « Perso », puis un minuteur de plus d'une heure (onglet, île réduite).
        SelectTab(Tab.Calendar, animate: false);
        CalendarTab.ShowCustomSample(new TimeSpan(1, 30, 0));
        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "2b-minuteur-perso.png"));
        CalendarTab.ShowToday();
        _timer.StartWith(new TimeSpan(23, 59, 59));
        _timer.StartPause(); // en pause : le rendu ne dépend pas de l'heure
        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "2c-minuteur-heures.png"));
        Collapse();
        FinishAnimations();
        await SnapshotRenderer.SaveAsync(this, Path.Combine(folder, "0d-minuteur-heures.png"));
        _timer.Reset();

        Collapse();
        FinishAnimations();
        await Palette.PaletteWindow.RenderSnapshotsAsync(this, folder);
        Close(); // l'app se ferme quand toutes les fenêtres de rendu ont fini
    }

    private void FinishAnimations()
    {
        _width.Snap();
        _height.Snap();
        _radius.Snap();
        ApplyGeometry();

        SetFinal(CompactView, _expanded || _gaming ? 0 : 1);
        SetFinal(ExpandedView, _expanded ? 1 : 0);
        foreach (var (element, target) in _fadeTargets) SetFinal(element, target);
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
        ExpandedScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
        ExpandedScale.ScaleX = ExpandedScale.ScaleY = _expanded ? 1 : 0.92;
        // Onglet ouvert visible en entier dans la rangée (comme après le ressort dans l'app).
        UpdateLayout();
        TabButtons().First(t => t.Tab == _tab).Button.BringIntoView();
        UpdateLayout();

        static void SetFinal(UIElement element, double opacity)
        {
            element.BeginAnimation(OpacityProperty, null);
            element.Opacity = opacity;
        }
    }

    /// <summary>Rendu : villes d'exemple si l'utilisateur n'en a pas (en mémoire seulement, --snapshot n'enregistre rien).</summary>
    private void ShowSampleClocks()
    {
        var clocks = SettingsStore.Current.Island.WorldClocks;
        if (clocks.Count == 0)
        {
            clocks.Add(new WorldClock { Name = "Paris, France", TimeZone = "Europe/Paris" });
            clocks.Add(new WorldClock { Name = "Tokyo, Japon", TimeZone = "Asia/Tokyo" });
            clocks.Add(new WorldClock { Name = "Vancouver, Canada", TimeZone = "America/Vancouver" });
            clocks.Add(new WorldClock { Name = "Sydney, Australie", TimeZone = "Australia/Sydney" });
        }
        ClocksTabButton.Visibility = Visibility.Visible;
        ClocksTab.Refresh();
    }
}
