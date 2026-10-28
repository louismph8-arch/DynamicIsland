namespace DynamicIsland;

/// <summary>
/// Nom affiché de l'app (5H, choisi par l'utilisateur). Seul le nom VISIBLE change : l'exe, le dossier
/// %APPDATA%\DynamicIsland, les valeurs du registre, les titres internes des fenêtres (« --quit » cherche la fenêtre
/// « Dynamic Island ») et l'espace de noms gardent « DynamicIsland », pour ne rien casser chez ceux qui l'ont déjà.
/// </summary>
public static class AppInfo
{
    public const string Name = "Opaline";
}
