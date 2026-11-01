# -*- coding: utf-8 -*-
"""Police des grands chiffres d'Opaline (O4) : sous-ensemble d'Inter Display.

Source officielle : https://github.com/rsms/inter/releases (Inter-4.1.zip, dossier extras/ttf),
licence SIL Open Font License 1.1 SANS nom de police réservé (LICENSE.txt du zip) : un sous-ensemble
peut garder le nom « Inter Display ».

On garde seulement le latin (accents français compris), la ponctuation, les espaces fines, quelques
symboles (°, %, €, flèches, signe moins) et les fonctions OpenType utiles (tnum = chiffres à
largeur fixe, crénage…). Le reste de l'interface reste en Segoe UI Variable : un caractère absent passe à Segoe UI (repli).

Utilisation (fontTools : python -m pip install fonttools) :
    python tools/subset-numbers-font.py <dossier où sont les InterDisplay-*.ttf d'origine>
Écrit src/DynamicIsland/Assets/Fonts/InterDisplay-<graisse>.ttf.
"""
import os
import sys

from fontTools import subset

WEIGHTS = ["ExtraLight", "Light", "Medium"]

# Fonctions OpenType gardées : chiffres à largeur fixe / proportionnels, crénage, deux-points centré entre
# deux chiffres (calt), capitales, accents (ccmp, mark), zéro barré. Les variantes stylistiques (ss01…, cv01…)
# sont retirées : elles doubleraient la taille du fichier pour rien.
FEATURES = ["tnum", "pnum", "kern", "calt", "case", "ccmp", "locl", "mark", "mkmk", "zero"]

UNICODES = [
    (0x0020, 0x007E),  # latin de base
    (0x00A0, 0x00FF),  # latin-1 : espace insécable, °, ±, ×, ÷, accents
    (0x0100, 0x017F),  # latin étendu A (œ, Œ…)
    (0x2000, 0x206F),  # ponctuation générale : espaces fines (U+202F), tirets, …, ′ ″
    (0x20AC, 0x20AC),  # €
    (0x2190, 0x2193),  # ← ↑ → ↓ (débits)
    (0x2212, 0x2212),  # signe moins
    (0x2215, 0x2215),  # barre de division
    (0x2236, 0x2236),  # deux-points de rapport
]


def main() -> int:
    if len(sys.argv) != 2:
        print(__doc__)
        return 2
    source = sys.argv[1]
    here = os.path.dirname(os.path.abspath(__file__))
    target = os.path.join(here, "..", "src", "DynamicIsland", "Assets", "Fonts")
    unicodes = [code for start, end in UNICODES for code in range(start, end + 1)]
    for weight in WEIGHTS:
        name = f"InterDisplay-{weight}.ttf"
        options = subset.Options()
        options.layout_features = FEATURES
        options.name_IDs = ["*"]              # noms d'origine (famille « Inter Display », graisse)
        options.name_languages = ["*"]
        options.notdef_outline = True
        options.glyph_names = False
        options.hinting = True                # Inter est « hinté » : on le garde pour les petites tailles
        font = subset.load_font(os.path.join(source, name), options)
        subsetter = subset.Subsetter(options)
        subsetter.populate(unicodes=unicodes)
        subsetter.subset(font)
        out = os.path.join(target, name)
        subset.save_font(font, out, options)
        print(f"{name} : {os.path.getsize(os.path.join(source, name)):,} -> {os.path.getsize(out):,} octets")
    return 0


if __name__ == "__main__":
    sys.exit(main())
