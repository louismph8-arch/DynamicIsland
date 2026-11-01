namespace DynamicIsland.Plugins;

/// <summary>
/// Widget des potes (7H) déjà vérifié : un fichier JSON DÉCLARATIF (aucun code n'est jamais exécuté). Voir docs/widgets.md
/// pour le format, <see cref="WidgetPackageReader"/> pour toutes les vérifications faites à l'import.
/// </summary>
public sealed class WidgetDefinition
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public string Author { get; init; } = "";
    public string Version { get; init; } = "1.0";
    public string Description { get; init; } = "";
    public double Width { get; init; }
    public double Height { get; init; }
    /// <summary>Carte de l'île derrière le widget (sinon fond transparent, comme l'horloge géante).</summary>
    public bool Card { get; init; } = true;
    public WebSourceDefinition? Source { get; init; }
    public required WidgetElement Root { get; init; }

    /// <summary>Tous les éléments (racine comprise), dans l'ordre.</summary>
    public IEnumerable<WidgetElement> Elements() => Walk(Root);

    private static IEnumerable<WidgetElement> Walk(WidgetElement element)
    {
        yield return element;
        foreach (var child in element.Children)
            foreach (var inner in Walk(child)) yield return inner;
    }

    /// <summary>Clés de données utilisées (« cpu », « weather.temp », « web.prix »…) : pour ne mesurer que ce qui sert.</summary>
    public IReadOnlySet<string> UsedKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        foreach (var element in Elements())
        {
            foreach (var text in new[] { element.Text, element.Icon })
                if (text != null) foreach (var (key, _) in WidgetTemplate.Placeholders(text)) keys.Add(key);
            if (element.Value != null) keys.Add(element.Value);
        }
        return keys;
    }
}

public enum WidgetElementType { Stack, Grid, Text, Icon, Image, Ring, Bar, Spacer }

/// <summary>Un élément du widget. Seules les propriétés de son type sont remplies (le lecteur refuse les autres).</summary>
public sealed class WidgetElement
{
    public WidgetElementType Type { get; init; }
    public IReadOnlyList<WidgetElement> Children { get; init; } = [];

    // Disposition (stack, grid)
    public bool Horizontal { get; init; }
    public int Columns { get; init; } = 2;
    public double Gap { get; init; }
    public double Padding { get; init; }
    /// <summary>start / center / end / stretch (alignement dans le parent, et du texte).</summary>
    public string Align { get; init; } = "start";

    // Texte et icône
    public string? Text { get; init; }
    /// <summary>Nom Lucide (« cloud-sun ») ou gabarit (« {weather.icon} »).</summary>
    public string? Icon { get; init; }
    public double Size { get; init; }
    public string Weight { get; init; } = "regular";
    public string Font { get; init; } = "ui";
    public string Color { get; init; } = "text";
    public int MaxLines { get; init; } = 1;

    // Image
    public string? Src { get; init; }
    public double Width { get; init; }
    public double Height { get; init; }
    public bool Round { get; init; }

    // Jauge, anneau, barre
    /// <summary>Clé de donnée (« cpu », « web.prix »).</summary>
    public string? Value { get; init; }
    public double Min { get; init; }
    public double Max { get; init; } = 100;
    public double Thickness { get; init; }
}

/// <summary>Source web facultative : GET https d'UNE adresse, réponse JSON dont on lit quelques champs.</summary>
public sealed class WebSourceDefinition
{
    public required Uri Url { get; init; }
    /// <summary>Nom d'hôte exact (en minuscules) : c'est lui que l'utilisateur autorise.</summary>
    public string Host => Url.IdnHost.ToLowerInvariant();
    /// <summary>Minutes entre deux lectures (5 au moins).</summary>
    public int EveryMinutes { get; init; } = 10;
    /// <summary>Nom du champ (« prix ») → chemin dans la réponse (« $.bitcoin.usd »).</summary>
    public required IReadOnlyDictionary<string, string> Fields { get; init; }
}

/// <summary>Widget lu : définition + images PNG du paquet (déjà vérifiées).</summary>
public sealed record WidgetPackage(WidgetDefinition Definition, IReadOnlyDictionary<string, byte[]> Images);
