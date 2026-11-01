# Widgets des potes / Friends' widgets

*English below.*

## Français

Un widget Opaline est un **fichier `.opalinewidget`** qui *décrit* ce qu'il faut afficher. Il ne contient **aucun code** :
Opaline le lit, le vérifie, puis le dessine lui-même. Un widget ne peut donc ni lire tes fichiers, ni lancer de
programme, ni faire planter l'app (au pire, il s'affiche « Widget indisponible »).

Il s'affiche **sur le bureau** (déplaçable, redimensionnable, aimanté à la grille, estompé quand une appli est devant)
et/ou **en bas de l'onglet Widgets de l'île**.

### Installer, créer

- Paramètres → Widgets → « Widgets des potes » → **Installer un widget…**, ou double-clic sur le fichier.
  Un aperçu montre le nom, l'auteur et le site web demandé avant d'installer.
- **Créer un widget** ouvre un dossier avec `mon-widget.opalinewidget` (modèle commenté), deux exemples et cette doc.
  Modifie le modèle dans un éditeur de texte (Bloc-notes, VS Code), enregistre, puis installe-le.
- Les widgets installés sont rangés dans `%APPDATA%\DynamicIsland\Widgets\<id>\`.

### Le fichier

Soit le JSON tout seul, soit un **.zip renommé en `.opalinewidget`** qui contient `widget.json` et jusqu'à 8 images PNG
(1 Mo chacune au plus, 2048 × 2048 pixels au plus). Les commentaires `//` sont permis.

```json
{
  "format": 1,
  "id": "mon-widget",
  "name": "Mon widget",
  "author": "Moi",
  "version": "1.0",
  "description": "Ce que fait le widget.",
  "size": { "width": 260, "height": 150 },
  "background": "card",
  "source": { ... },
  "root": { ... }
}
```

| Champ | Obligatoire | Règle |
|---|---|---|
| `format` | oui | toujours `1` |
| `id` | oui | 2 à 40 caractères : minuscules, chiffres, tirets (`mon-widget`) |
| `name` | oui | 60 caractères au plus |
| `author`, `version`, `description` | non | 60 / 20 / 200 caractères au plus |
| `size` | non | `width` 80–640, `height` 40–480 (défaut 240 × 120) |
| `background` | non | `card` (carte de l'île, défaut) ou `none` (transparent) |
| `source` | non | source web, voir plus bas |
| `root` | oui | le premier élément (en général une pile) |

### Éléments

Chaque élément a un `type` et peut avoir `align` : `start` (défaut), `center`, `end`, `stretch`.

| Type | Propriétés |
|---|---|
| `stack` | `direction` (`vertical` / `horizontal`), `gap` 0–48, `padding` 0–48, `children` |
| `grid` | `columns` 1–6, `gap`, `padding`, `children` |
| `text` | `text` (200 caractères, gabarits permis), `size`, `weight` (`light`, `regular`, `semibold`, `bold`), `font` (`ui`, `display`), `color`, `lines` 1–4 |
| `icon` | `name` = nom d'icône [Lucide](https://lucide.dev/icons) (`cloud-sun`, `bitcoin`…) ou `{weather.icon}`, `size`, `color` |
| `image` | `src` (PNG du paquet), `width`, `height`, `round` (coins arrondis) |
| `ring` | anneau : `value` (donnée chiffrée), `min`, `max` (défaut 0–100), `size` 24–240, `thickness` 2–24, `color`, `text` au centre |
| `bar` | barre : `value`, `min`, `max`, `height` 2–24, `width` (0 = toute la largeur), `color` |
| `spacer` | espace vide : `size` |

- Tailles de texte : `hero`, `display`, `large-title`, `title1`, `title2`, `title3`, `headline`, `body`, `callout`,
  `footnote`, `caption`, `caption2`, ou un nombre (6–160).
- Tailles d'icône : `xs`, `s`, `m`, `l`, `xl`, `xxl`, `huge`, ou un nombre.
- Couleurs : `accent` (couleur d'Opaline, suit le profil), `accent-start`, `accent-end`, `text`, `secondary`, `tertiary`,
  `white`, `red`, `orange`, `green`, ou `#RRGGBB` / `#AARRGGBB`.
- Limites : 40 éléments au plus, 4 niveaux de piles / grilles imbriquées au plus.

### Gabarits (données d'Opaline)

Dans un texte, `{clé}` ou `{clé:format}` est remplacé par la donnée. Une clé inconnue reste affichée telle quelle.

| Clé | Exemple |
|---|---|
| `{time}`, `{time:HH:mm:ss}` | 14:05 |
| `{date}`, `{date:dddd}`, `{date:d MMMM yyyy}` | samedi |
| `{weather.temp}`, `{weather.min}`, `{weather.max}` | 6° |
| `{weather.city}`, `{weather.desc}`, `{weather.icon}` | Montréal, Partiellement nuageux, (icône) |
| `{music.title}`, `{music.artist}`, `{music.app}` | morceau en cours |
| `{cpu}`, `{ram}`, `{gpu}` | 23 % |
| `{gpu.temp}` | 54 °C (cartes NVIDIA) |
| `{net.down}`, `{net.up}` | débits du réseau |
| `{friends.online}` | amis en ligne (Amis en ligne d'Opaline) |
| `{web.<champ>}`, `{web.<champ>:n0}` | champ de la source web |

Données chiffrées pour `ring` et `bar` : `cpu`, `ram`, `gpu`, `gpu.temp`, `weather.temp`, `friends.online`, `web.<champ>`.
Formats de nombre : `n0`…`n4` (séparateurs de milliers), `f0`…`f4`, `p0`…`p4`, `0`, `0.0`, `0.00`.

### Source web (facultative)

```json
"source": {
  "url": "https://api.coingecko.com/api/v3/simple/price?ids=bitcoin&vs_currencies=usd",
  "every": 10,
  "fields": { "usd": "$.bitcoin.usd" }
}
```

- **https** seulement, un vrai nom de site (pas d'adresse IP, de réseau local, de port ni d'identifiants).
- Opaline ne la lit **qu'après ton accord pour ce site précis** (case à cocher à l'installation, retirable dans la liste
  des widgets). Sans accord, les champs s'affichent « — ».
- Un simple GET, **sans cookies ni identifiants**, sans suivre de redirection ; réponse JSON de 64 Ko au plus ;
  toutes les `every` minutes (5 au minimum, 10 par défaut), seulement quand le widget est affiché.
- `fields` : 10 champs au plus ; chemin simple dans la réponse : `$.a.b`, `$.liste[0].nom`.

---

## English

An Opaline widget is a **`.opalinewidget` file** that *describes* what to show. It contains **no code**: Opaline reads it,
checks it, then draws it itself. A widget can't read your files, run programs or crash the app (at worst it shows
"Widget unavailable"). It can be shown **on the desktop** and/or **at the bottom of the island's Widgets tab**.

**Install / create**: Settings → Widgets → "Friends' widgets" → **Install a widget…** (or double-click the file); a preview
shows the name, the author and the requested website first. **Create a widget** opens a folder with a commented template,
two examples and these docs. Installed widgets live in `%APPDATA%\DynamicIsland\Widgets\<id>\`.

**File**: the JSON alone, or a **.zip renamed `.opalinewidget`** with `widget.json` and up to 8 PNG images (1 MB and
2048 × 2048 px each at most). `//` comments are allowed. Top-level fields, elements, sizes, colours, templates and limits
are exactly the ones in the French tables above:

- Top level: `format` (always 1), `id` (lowercase, digits, dashes), `name`, optional `author`, `version`, `description`,
  `size` (`width` 80–640, `height` 40–480), `background` (`card` / `none`), `source`, and `root`.
- Elements: `stack`, `grid`, `text`, `icon` (a [Lucide](https://lucide.dev/icons) name), `image`, `ring`, `bar`, `spacer`;
  at most 40 elements and 4 nested layouts; texts up to 200 characters.
- Templates: `{time:HH:mm}`, `{date:dddd}`, `{weather.temp}`, `{weather.city}`, `{weather.icon}`, `{music.title}`, `{cpu}`,
  `{ram}`, `{gpu}`, `{gpu.temp}`, `{net.down}`, `{net.up}`, `{friends.online}`, `{web.<field>}`. Unknown templates are shown
  as they are. Numeric values for `ring` / `bar`: `cpu`, `ram`, `gpu`, `gpu.temp`, `weather.temp`, `friends.online`,
  `web.<field>`.
- Web source (optional): one **https** GET to the declared address, **only after you allow that exact website** (checkbox at
  install time, revocable), no cookies or credentials, no redirects, JSON response up to 64 KB, every `every` minutes
  (5 at least), only while the widget is shown. Fields are read with simple paths such as `$.data.price` or `$.items[0].name`.
