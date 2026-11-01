namespace DynamicIsland.Settings;

/// <summary>
/// Widgets des potes (7H). Les fichiers des widgets sont rangés dans %APPDATA%\DynamicIsland\Widgets\&lt;id&gt;\ ; ici, seulement
/// les choix de l'utilisateur : où chacun s'affiche, sa place sur le bureau et le site web qu'il a autorisé.
/// </summary>
public sealed class PluginSettings
{
    public const int MaxItems = 30;

    public List<PluginState> Items { get; set; } = [];

    /// <summary>Choix d'un widget installé (créé à l'installation s'il manque).</summary>
    public PluginState For(string id)
    {
        var item = Items.FirstOrDefault(i => i.Id == id);
        if (item != null) return item;
        item = new PluginState { Id = id };
        Items.Add(item);
        return item;
    }

    public void Normalize()
    {
        Items = (Items ?? []).Where(i => i != null && Plugins.WidgetPackageReader.IsValidId(i.Id))
            .GroupBy(i => i.Id).Select(g => g.First()).Take(MaxItems).ToList();
        foreach (var item in Items)
        {
            item.Scale = Math.Clamp(item.Scale, Themes.Desk.MinScale, Themes.Desk.MaxScale);
            item.AllowedHost = string.IsNullOrWhiteSpace(item.AllowedHost) ? null : item.AllowedHost.Trim().ToLowerInvariant();
        }
    }
}

public sealed class PluginState
{
    public string Id { get; set; } = "";
    /// <summary>Affiché sur le bureau (même fenêtre que les widgets du bureau de la 5E).</summary>
    public bool OnDesktop { get; set; }
    /// <summary>Affiché en bas de l'onglet Widgets de l'île.</summary>
    public bool InIsland { get; set; } = true;
    public double? Left { get; set; }
    public double? Top { get; set; }
    public double Scale { get; set; } = 1;
    /// <summary>
    /// Site que l'utilisateur a autorisé pour la source web du widget (nom d'hôte exact, en minuscules), ou null. Une mise à
    /// jour du widget qui demande un AUTRE site ne garde pas cet accord.
    /// </summary>
    public string? AllowedHost { get; set; }
}
