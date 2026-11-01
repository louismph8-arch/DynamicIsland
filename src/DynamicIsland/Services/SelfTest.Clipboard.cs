using System.IO;
using System.IO.Compression;
using System.Text;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test du presse-papiers et du coffre : tout en mémoire ou dans un dossier temporaire effacé à la fin. Le VRAI
/// presse-papiers n'est jamais touché, le vrai coffre (coffre.bin) ni le vrai historique ne sont jamais lus ni écrits.
/// </summary>
internal static partial class SelfTest
{
    private static void ClipboardAndVault(List<string> lines)
    {
        // Réglages par défaut (demande de l'utilisateur : tout reste ; l'effacement après 30 s est une option).
        var defaults = new ClipboardSettings();
        Check(lines, defaults.History && defaults.Persist && !defaults.AutoClear && defaults.OfferSecrets,
            "par défaut : historique gardé après redémarrage, proposition du coffre, effacement après 30 s désactivé");
        Check(lines, IslandTabItem.Defaults().Any(t => t.Id == IslandTabItem.Clipboard && t.Visible), "onglet « Presse-papiers » visible par défaut");

        // Formats sensibles (ce que posent les gestionnaires de mots de passe).
        static Func<string, bool> Has(params string[] formats) => f => formats.Contains(f);
        static Func<string, uint?> Dword(string? format, uint value) => f => f == format ? value : null;
        bool exclude = SecureClipboard.IsSensitive(Has(SecureClipboard.ExcludeFormat), Dword(null, 0));
        bool noHistory = SecureClipboard.IsSensitive(Has(), Dword(SecureClipboard.HistoryFormat, 0));
        bool noCloud = SecureClipboard.IsSensitive(Has(), Dword(SecureClipboard.CloudFormat, 0));
        bool allowed = SecureClipboard.IsSensitive(Has(), Dword(SecureClipboard.HistoryFormat, 1));
        bool plain = SecureClipboard.IsSensitive(Has(), Dword(null, 0));
        Check(lines, exclude && noHistory && noCloud && !allowed && !plain,
            "copie sensible reconnue : ExcludeClipboardContentFromMonitorProcessing, CanIncludeInClipboardHistory = 0, CanUploadToCloudClipboard = 0 ; copie ordinaire non");
        var secret = SecureClipboard.SecretData("x");
        Check(lines, secret.GetDataPresent(SecureClipboard.ExcludeFormat) && secret.GetDataPresent(SecureClipboard.HistoryFormat)
                     && secret.GetDataPresent(SecureClipboard.CloudFormat) && secret.GetText() == "x",
            "mot de passe du coffre copié avec les 3 formats d'exclusion (objet construit en mémoire)");

        // Proposition « Garder dans le coffre » : jamais dans l'historique, oubliée après le délai.
        var store = new ClipboardHistoryStore { OfferLifetime = TimeSpan.FromMilliseconds(250) };
        bool offered = store.Offer("Mot-De-Passe-Essai!");
        bool during = store.HasSecret;
        Thread.Sleep(600);
        Check(lines, offered && during && !store.HasSecret && store.Count == 0, "mot de passe proposé : rien dans l'historique, oublié après le délai");
        store.Offer("Autre-Essai");
        var taken = store.TakeSecret();
        Check(lines, taken == "Autre-Essai" && store.TakeSecret() == null && store.Count == 0, "« Garder dans le coffre » rend le mot de passe une seule fois");
        Check(lines, !store.Offer("ligne 1\nligne 2") && !store.Offer(new string('a', 300)), "texte sur plusieurs lignes ou trop long : pas pris pour un mot de passe");

        // Historique : doublons, liens, garde-fou (2000 textes, 300 images), épinglés jamais retirés.
        store.AddText("bonjour");
        store.AddText("https://exemple.com/page");
        store.AddText("bonjour");
        var items = store.Items;
        Check(lines, items.Count == 2 && items[0].Text == "bonjour" && items[1].Kind == ClipKind.Link, "doublon remonté en haut, lien reconnu");
        store.Pin(items[1].Id, true);
        for (int i = 0; i < ClipboardHistoryStore.MaxTexts + 50; i++) store.AddText("texte " + i);
        for (int i = 0; i < ClipboardHistoryStore.MaxImages + 20; i++) store.AddImage(BitConverter.GetBytes(i), 4, 4);
        items = store.Items;
        int texts = items.Count(i => i.Kind != ClipKind.Image), images = items.Count(i => i.Kind == ClipKind.Image);
        Check(lines, texts == ClipboardHistoryStore.MaxTexts && images == ClipboardHistoryStore.MaxImages && items.Any(i => i.Pinned && i.Text.StartsWith("https://")),
            $"garde-fou : {texts} textes et {images} images gardés au plus, les plus anciens non épinglés partent, l'épinglé reste");
        store.Clear();
        Check(lines, store.Count == 1 && store.Items[0].Pinned, "« Tout effacer » garde les épinglés");

        // Persistance : aller-retour chiffré DPAPI (fichier temporaire, écriture atomique).
        var folder = Path.Combine(Path.GetTempPath(), "opaline-selftest-clip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            store.AddText("à garder après redémarrage");
            store.AddImage([1, 2, 3, 4], 2, 2);
            var historyFile = Path.Combine(folder, "presse-papiers.bin");
            ClipboardHistory.WriteAtomic(historyFile, ClipboardHistory.Protect(store.Serialize()));
            ClipboardHistory.WriteAtomic(historyFile, ClipboardHistory.Protect(store.Serialize())); // remplacement
            var raw = File.ReadAllBytes(historyFile);
            var reloaded = new ClipboardHistoryStore();
            reloaded.Load(ClipboardHistory.Unprotect(raw));
            var a = store.Items;
            var b = reloaded.Items;
            Check(lines, !Contains(raw, "à garder après redémarrage") && a.Count == b.Count
                         && a.Zip(b).All(p => p.First.Text == p.Second.Text && p.First.Pinned == p.Second.Pinned && p.First.Kind == p.Second.Kind
                                              && (p.First.Png ?? []).SequenceEqual(p.Second.Png ?? [])),
                $"historique gardé après redémarrage : {b.Count} éléments relus à l'identique, fichier chiffré (texte illisible)");

            // Coffre : aller-retour DPAPI dans un fichier temporaire.
            const string password = "Opaline-Essai-7f3K9!x", site = "SiteDEssaiOpaline";
            var vaultFile = Path.Combine(folder, "coffre.bin");
            VaultFile.Save(vaultFile, [new VaultEntry { Name = site, Login = "essai@exemple.com", Password = password, Note = "note" }]);
            var vaultRaw = File.ReadAllBytes(vaultFile);
            var entries = VaultFile.Load(vaultFile);
            Check(lines, entries.Count == 1 && entries[0].Password == password && entries[0].Name == site && !Contains(vaultRaw, password) && !Contains(vaultRaw, site),
                "coffre : aller-retour chiffré (DPAPI, ce compte), ni le mot de passe ni le nom lisibles dans le fichier");

            // Rapport de bug : aucune donnée du coffre (ni réglages, ni journaux).
            var report = BugReport.Create(folder);
            string all;
            using (var zip = ZipFile.OpenRead(report))
                all = string.Concat(zip.Entries.Select(e =>
                {
                    using var reader = new StreamReader(e.Open(), Encoding.UTF8);
                    return reader.ReadToEnd();
                }));
            Check(lines, !all.Contains(password) && !all.Contains(site) && !all.Contains("essai@exemple.com") && !SettingsStore.ToJson().Contains(password),
                "rapport de bug et réglages : aucune donnée du coffre");
        }
        finally
        {
            Directory.Delete(folder, recursive: true);
        }

        // Effacement après 30 s (ici 200 ms, fonctions simulées : le vrai presse-papiers n'est pas touché).
        uint sequence = 10;
        int cleared = 0;
        var eraser = new SecretEraser(() => sequence, () => cleared++);
        eraser.Arm(10, TimeSpan.FromMilliseconds(200));
        Thread.Sleep(500);
        bool clearedWhenSame = cleared == 1;
        eraser.Arm(10, TimeSpan.FromMilliseconds(200));
        sequence = 11; // quelqu'un a copié autre chose entre-temps
        Thread.Sleep(500);
        bool keptWhenChanged = cleared == 1;
        sequence = 10;
        eraser.Arm(10, TimeSpan.FromMilliseconds(200));
        eraser.Disarm(); // option coupée / autre copie de l'app
        Thread.Sleep(500);
        Check(lines, clearedWhenSame && keptWhenChanged && cleared == 1 && !eraser.IsArmed,
            "effacement : seulement si le presse-papiers n'a pas changé depuis la copie, rien si désarmé");
    }

    private static bool Contains(byte[] data, string text) =>
        Encoding.UTF8.GetString(data).Contains(text) || Encoding.Unicode.GetString(data).Contains(text);
}
