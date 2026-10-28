"""Textes FR / EN (6A) : chaque clé utilisée (Loc.T / Loc.F, {ui:L}) doit exister en français ET en anglais, avec les
mêmes {0}, {1}… Usage, depuis le dossier DynamicIsland :  python tools\\check-loc.py   (code de sortie 1 si un problème)."""
import json
import os
import re
import sys

ROOT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "DynamicIsland")
fr = json.load(open(os.path.join(ROOT, "Localization", "fr.json"), encoding="utf-8"))
en = json.load(open(os.path.join(ROOT, "Localization", "en.json"), encoding="utf-8"))
KEY = re.compile(r'"([a-z][a-z0-9_]*(?:\.[a-z0-9_]+)+)"')

used = {}
for folder, _, files in os.walk(ROOT):
    if os.sep + "obj" in folder or os.sep + "bin" in folder:
        continue
    for name in files:
        path = os.path.join(folder, name)
        if name.endswith(".xaml"):
            for m in re.finditer(r"\{ui:L ([^}]+)\}", open(path, encoding="utf-8-sig").read()):
                used.setdefault(m.group(1).strip(), path)
        elif name.endswith(".cs"):
            source = open(path, encoding="utf-8-sig").read()
            for m in re.finditer(r"Loc\.[TF]\(", source):
                depth, i = 1, m.end()
                while i < len(source) and depth:
                    depth += {"(": 1, ")": -1}.get(source[i], 0)
                    i += 1
                for key in KEY.findall(source[m.end():i]):
                    used.setdefault(key, path)
            # Clés construites : Loc.T($"settings.status.{id}…") → toutes les clés qui commencent par ce préfixe.
            for prefix in re.findall(r'Loc\.[TF]\(\$"([a-z0-9_.]+)\{', source):
                for key in fr:
                    if key.startswith(prefix):
                        used.setdefault(key, path)
            # Clés rangées dans des tableaux et traduites plus loin (modèles de Claude, pastilles, icônes de profil).
            for key in re.findall(r'"((?:claude\.model|swatch|icon)\.[a-z_]+)"', source):
                used.setdefault(key, path)

missing_fr = sorted(k for k in used if k not in fr)
missing_en = sorted(k for k in fr if k not in en)
extra_en = sorted(k for k in en if k not in fr)
unused = sorted(k for k in fr if k not in used and not k.startswith(("module.", "text.", "format.")))
holes = sorted(k for k in fr if len(re.findall(r"\{\d", fr[k])) != len(re.findall(r"\{\d", en.get(k, ""))))

print(f"{len(fr)} clés en français, {len(en)} en anglais, {len(used)} utilisées")
print("absentes de fr.json :", [(k, os.path.basename(used[k])) for k in missing_fr])
print("absentes de en.json :", missing_en)
print("en.json en trop :", extra_en)
print("nombre de {…} différent :", holes)
print("inutilisées (à vérifier) :", unused)
sys.exit(1 if missing_fr or missing_en or extra_en or holes else 0)
