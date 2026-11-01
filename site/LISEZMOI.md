# Page de présentation d'Opaline (GitHub Pages)

Page statique : `index.html`, `style.css`, `app.js`, images dans `img/` (~0,33 Mo en tout). Aucune ressource externe
(pas de CDN, pas de polices en ligne, pas de traceur, pas de cookie). Seul appel réseau : `app.js` lit
`api.github.com/repos/louismph8-arch/DynamicIsland/releases/latest` dans le navigateur du visiteur (version, taille, date,
SHA-256 de `Opaline-Setup.exe`) ; si ça échoue, un texte de repli reste affiché et les boutons visent la page de la dernière
version. FR / EN : langue du navigateur par défaut, bouton en haut, `?lang=fr|en`.

## Essayer en local

```powershell
python -m http.server 8765 --bind 127.0.0.1 --directory public\site
# puis ouvrir http://127.0.0.1:8765/
```

(Ouvrir directement `index.html` marche aussi, sauf dans les navigateurs qui bloquent les fichiers locaux.)

## Mettre en ligne (seulement avec l'accord de Louis)

`tools/publish-source.ps1` recopie `public/*` à la racine du dépôt public, donc cette page s'y retrouve dans `site/`.
Deux possibilités :

1. **Dossier `/site` de la branche `main`** : GitHub Pages ne sait servir que la racine ou `/docs`. Il faut donc soit
   renommer le dossier en `docs/` dans le dépôt public, soit publier par GitHub Actions (« Source : GitHub Actions » +
   un petit workflow `actions/upload-pages-artifact` avec `path: site`).
2. **Branche `gh-pages`** : copier le contenu de `site/` à la racine d'une branche `gh-pages` du dépôt public, puis
   Settings → Pages → « Deploy from a branch » → `gh-pages` / `(root)`.

Adresse finale : `https://louismph8-arch.github.io/DynamicIsland/`.

## Images

Toutes viennent de `DynamicIsland.exe --snapshot <dossier> --settings <{"Language":"fr|en","WelcomeDone":true}> --demo`
(données d'exemple, jamais les vraies), sauf le dock et la télécommande repris de `public/images` (déjà publiés), le logo
(`--render-logo`) et le fond « Aurore » (dessiné par l'app). Ne jamais refaire ces captures sans `--demo`. Le dock et
l'onglet Contrôles d'un rendu `--demo` montrent les vraies applis / les vrais appareils du PC : ne pas les utiliser.

## Avant la mise en ligne

- Vérifier que les « Issues » sont activées sur le dépôt public (lien « Signaler un bug »), sinon changer le lien.
- La configuration requise dit « Windows 11 ou Windows 10 (2004+) » comme le README : à confirmer.
