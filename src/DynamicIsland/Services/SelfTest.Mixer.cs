using DynamicIsland.Dock;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du mélangeur de volume (point 22) : regroupement par programme (calcul pur) et vraies sessions audio LUES
/// seulement — aucun volume ni aucune sourdine n'est jamais changé pendant le test.
/// </summary>
internal static partial class SelfTest
{
    private static void Mixer(List<string> lines)
    {
        var merged = AudioMixerService.Merge(
        [
            new("chrome", "Google Chrome", @"C:\chrome.exe", false, 0.4, true, 0),
            new("chrome", "Google Chrome", null, false, 0.7, false, 0.3f),
            new(AudioMixerService.SystemKey, "Sons système", null, true, 1, false, 0.5f),
            new("vlc", "VLC", null, false, 0.5, true, 0),
            new("spotify", "Spotify", null, false, 0.9, false, 0.2f),
        ]);
        Check(lines, merged.Count == 4 && merged[0].Key == "chrome" && merged[0].Level == 0.7 && !merged[0].Muted && merged[0].ExePath == @"C:\chrome.exe"
                     && merged[1].Key == "spotify" && merged[2].Key == "vlc" && merged[2].Muted && merged[3].SystemSounds,
            "mélangeur : sessions d'un même programme regroupées (volume le plus haut, muet seulement si toutes le sont), celles qui jouent d'abord, sons système en dernier");

        // Vraies sessions : deux lectures de suite, rien n'est réglé entre les deux.
        var mixer = new AudioMixerService();
        var first = mixer.Read();
        var second = mixer.Read();
        mixer.Release();
        bool sane = first.All(e => e.Level is >= 0 and <= 1 && e.Name.Length > 0) && first.Select(e => e.Key).Distinct().Count() == first.Count;
        bool unchanged = first.Where(a => second.Any(b => b.Key == a.Key)).All(a => second.First(b => b.Key == a.Key) is var b && Math.Abs(a.Level - b.Level) < 0.0001 && a.Muted == b.Muted);
        Check(lines, sane && unchanged,
            $"mélangeur : {first.Count} appli(s) avec du son lues sans rien changer" + (first.Count > 0 ? " — " + string.Join(", ", first.Take(5).Select(e => $"{e.Name} {Math.Round(e.Level * 100)} %{(e.Muted ? " (muet)" : "")}")) : ""));

        var tabs = IslandTabItem.NormalizeList([new IslandTabItem { Id = IslandTabItem.Music }]);
        var status = StatusItem.NormalizeList([new StatusItem { Id = StatusItem.Trash }]);
        Check(lines, tabs.Any(t => t.Id == IslandTabItem.Mixer && t.Visible) && status.Any(s => s.Id == StatusItem.Mixer && s.Visible)
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Views.MixerView.ShowApps()") == ModuleGuard.MixerTab
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Services.AudioMixerService.Read()") == ModuleGuard.MixerTab,
            "mélangeur : onglet et case du dock ajoutés aux anciens réglages, erreur attribuée au module « tab.mixer » (pas à l'onglet Musique)");
    }
}
