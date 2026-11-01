namespace DynamicIsland.Settings;

/// <summary>
/// Onglet « Traduire » (demandé le 2026-11-01) : textes traduits par l'assistant IA déjà réglé (moteur actif), pages web par
/// Google Traduction. Seulement des choix : aucun texte ni aucune traduction n'est gardé sur le disque.
/// </summary>
public sealed class TranslateSettings
{
    /// <summary>Langue cible (code de <see cref="Services.TranslateService.Languages"/>) ; vide = la langue de l'interface.</summary>
    public string Target { get; set; } = "";
    /// <summary>
    /// Raccourci global « Traduire ce que j'ai copié » ; vide = aucun. Ctrl+Alt+T par défaut : AltGr+T ne tape aucun caractère
    /// sur les claviers français (France), français (Canada) et anglais (États-Unis) — vérifié par <c>--self-test</c>.
    /// </summary>
    public string Hotkey { get; set; } = Services.Hotkeys.DefaultTranslate;

    public void Normalize()
    {
        Target = (Target ?? "").Trim();
        if (Target.Length > 0 && Services.TranslateService.Find(Target) == null) Target = "";
        Hotkey ??= "";
        if (Hotkey.Length > 0 && !Services.Hotkeys.TryParse(Hotkey, out _, out _)) Hotkey = "";
    }
}
