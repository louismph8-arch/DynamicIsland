using System.IO;
using DynamicIsland.Settings;

namespace DynamicIsland.Services;

/// <summary>
/// Auto-test des notes et du texte ajouté à la main au presse-papiers : tout en mémoire ou dans un dossier temporaire effacé à
/// la fin. Le vrai notes.json, le vrai historique et le VRAI presse-papiers ne sont jamais lus ni écrits.
/// </summary>
internal static partial class SelfTest
{
    private static void NotesAndClipText(List<string> lines)
    {
        // Réglages et onglet.
        var defaults = new NotesSettings();
        var bad = new NotesSettings { Hotkey = "pas un raccourci" };
        bad.Normalize();
        Check(lines, defaults.Hotkey == "" && !defaults.Encrypt && bad.Hotkey == ""
                     && IslandTabItem.Defaults().Any(t => t.Id == IslandTabItem.Notes && t.Visible)
                     && IslandTabItem.NormalizeList([new IslandTabItem { Id = IslandTabItem.Music }]).Any(t => t.Id == IslandTabItem.Notes),
            "notes : raccourci « Nouvelle note » vide par défaut, pas chiffrées par défaut, onglet ajouté aux anciens réglages");
        Check(lines, ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Views.NotesView.Refresh()") == ModuleGuard.NotesTab
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Notes.NoteEditorWindow.Commit()") == ModuleGuard.NotesTab
                     && ModuleGuard.OwnerOf("System.Exception: x\r\n   at DynamicIsland.Services.NotesStore.Add()") == ModuleGuard.NotesTab,
            "notes : une erreur est attribuée au module « tab.notes » (l'île continue)");

        // Cases à cocher.
        var text = "Courses\n- [ ] lait\n  * [x] pain\nrien\n- [] pas une case";
        var toggled = NoteText.ToggleTask(text, 1);
        var back = NoteText.ToggleTask(toggled, 1);
        Check(lines, NoteText.Lines(toggled)[1] == "- [x] lait" && back == text && NoteText.ToggleTask(text, 3) == text && NoteText.ToggleTask(text, 4) == text
                     && NoteText.ToggleTask(text, 99) == text && NoteText.ToggleTask(text, 2).Split('\n')[2] == "  * [ ] pain"
                     && NoteText.Progress(text) == (1, 2) && NoteText.Task("- [X] Fait").Done,
            "notes : « - [ ] » / « * [x] » cochés et décochés (aller-retour identique), une ligne normale ou « - [] » ne change pas, 1/2 cases");
        var untitled = new Note { Text = "\n# Idée **vive**\n- [ ] essayer" };
        var titled = new Note { Title = "Titre", Text = "ligne 1\nligne 2" };
        Check(lines, untitled.DisplayTitle == "Idée vive" && untitled.PreviewLines().Select(p => p.Index).SequenceEqual([2])
                     && titled.DisplayTitle == "Titre" && titled.PreviewLines().Count() == 2,
            "notes : sans titre, la 1re ligne non vide sert de titre (sans # ni **) et n'est pas répétée dans l'aperçu");

        // Ordre, limite, aller-retour du fichier (dossier temporaire).
        var store = new NotesStore();
        var a = store.Add("A", "")!;
        Thread.Sleep(15);
        var b = store.Add("B", "")!;
        Thread.Sleep(15);
        var c = store.Add("C", "", pinned: true, color: "yellow")!;
        Thread.Sleep(15);
        store.Update(a.Id, "A", "modifiée");
        var order = store.Items.Select(n => n.Title).ToList();
        Check(lines, order.SequenceEqual(["C", "A", "B"]), "notes : épinglées d'abord, puis la plus récemment modifiée");
        store.ToggleTask(b.Id, 0); // pas une case : rien ne bouge
        Check(lines, store.Get(b.Id)!.Text == "" && store.Get(c.Id)!.Color == "yellow", "notes : couleur gardée, case absente = texte inchangé");

        var full = new NotesStore();
        for (int i = 0; i < NotesStore.MaxNotes; i++) full.Add("n" + i, "");
        var refused = full.Add("de trop", "");
        Check(lines, refused == null && full.Count == NotesStore.MaxNotes && full.IsFull,
            $"notes : {NotesStore.MaxNotes} au plus (la suivante est refusée, aucune n'est effacée toute seule)");

        var folder = Path.Combine(Path.GetTempPath(), "Opaline-test-notes-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var plain = Path.Combine(folder, "notes.json");
            var crypt = Path.Combine(folder, "notes.bin");
            store.Update(b.Id, "B", "- [ ] accentué é ✓\n**gras**");
            ClipboardHistory.WriteAtomic(plain, store.Serialize());
            ClipboardHistory.WriteAtomic(crypt, NotesService.Protect(store.Serialize()));
            var fromPlain = new NotesStore();
            fromPlain.Load(File.ReadAllBytes(plain));
            var fromCrypt = new NotesStore();
            fromCrypt.Load(NotesService.Unprotect(File.ReadAllBytes(crypt)));
            bool same = fromPlain.Items.Select(n => (n.Id, n.Title, n.Text, n.Pinned, n.Color))
                .SequenceEqual(store.Items.Select(n => (n.Id, n.Title, n.Text, n.Pinned, n.Color)));
            bool sameCrypt = fromCrypt.Items.Select(n => n.Text).SequenceEqual(store.Items.Select(n => n.Text));
            bool hidden = !File.ReadAllText(crypt).Contains("accentu");
            Check(lines, same && sameCrypt && hidden && !File.Exists(plain + ".tmp"),
                "notes : aller-retour du fichier identique (JSON et chiffré DPAPI illisible en clair), écriture atomique");
        }
        finally
        {
            try
            {
                Directory.Delete(folder, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        // Texte écrit à la main dans le presse-papiers (en mémoire : le vrai presse-papiers n'est pas touché).
        var clips = new ClipboardHistoryStore();
        clips.AddText("ancien");
        var added = clips.AddManual("Mon texte\nsur deux lignes", pinned: true);
        var again = clips.AddManual("ancien", pinned: false);
        var items = clips.Items;
        Check(lines, added != null && again != null && items.Count == 2 && items[0].Text == "ancien" && items[1].Pinned && clips.AddManual("   ", true) == null,
            "presse-papiers : « + Nouveau » ajoute en tête (épinglé si demandé), un texte déjà là remonte, vide refusé");
        var editedKind = clips.Edit(added!.Id, "https://exemple.com", pinned: false)?.Kind; // l'élément est modifié en place
        var merged = clips.Edit(added.Id, "ancien", pinned: true);
        items = clips.Items;
        Check(lines, editedKind == ClipKind.Link && merged != null && items.Count == 1 && items[0].Pinned && items[0].Text == "ancien"
                     && clips.Edit("inconnu", "x", false) == null,
            "presse-papiers : « Modifier » change le texte (lien reconnu), un doublon est fusionné, élément inconnu refusé");
    }
}
