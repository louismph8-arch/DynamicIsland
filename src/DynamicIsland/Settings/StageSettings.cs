namespace DynamicIsland.Settings;

/// <summary>
/// Stage Manager (7G), désactivé par défaut : sur chaque écran, la fenêtre active au centre, les autres réduites et montrées en
/// miniatures dans une colonne à gauche. Les applis à laisser tranquilles sont dans les règles par application
/// (<see cref="AppRule.StageIgnore"/>).
/// </summary>
public sealed class StageSettings
{
    public bool Enabled { get; set; }
    /// <summary>La fenêtre du centre remplit toute la scène (zone de travail moins la colonne) — demandé par l'utilisateur.</summary>
    public bool FillStage { get; set; } = true;
    /// <summary>Colonne cachée hors de l'écran, révélée par la souris au bord gauche (demandé par l'utilisateur : « la colonne qui reste
    /// ouverte tout le temps, c'est un peu chiant »). La scène prend alors toute la zone de travail.</summary>
    public bool AutoHide { get; set; } = true;
    /// <summary>Raccourci « Stage Manager : activer / couper » (vide = aucun, comme au départ).</summary>
    public string Hotkey { get; set; } = "";

    public void Normalize()
    {
        Hotkey ??= "";
        if (Hotkey.Length > 0 && !Services.Hotkeys.TryParse(Hotkey, out _, out _)) Hotkey = "";
    }
}
