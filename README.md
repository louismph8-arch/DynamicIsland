# Dynamic Island pour Windows

Une « Dynamic Island » façon iPhone en haut de l'écran, un dock façon macOS en bas, et plein de petits
outils autour. Pour Windows 10 (2004+) et Windows 11, 64 bits.

## Installer

1. Télécharge **`DynamicIsland-Setup.exe`** dans la [dernière version](../../releases/latest).
2. Lance-le. Windows peut afficher « Windows a protégé votre ordinateur » (l'installeur n'est pas signé) :
   clique **Informations complémentaires** puis **Exécuter quand même**.
3. Pas besoin de droits administrateur ni d'installer .NET : tout est inclus.

L'app se met ensuite à jour toute seule : une nouvelle version s'annonce dans l'île, et elle ne s'installe
que si tu cliques « Installer ».

Pour la retirer : Paramètres Windows → Applications → **Dynamic Island** → Désinstaller (la barre des
tâches Windows revient automatiquement).

## Ce qu'elle fait

- **L'île** : heure, musique (pochette, égaliseur), calendrier et minuteur / Pomodoro, contrôles (volume,
  luminosité, mode sombre), widgets (météo, CPU, RAM, GPU, réseau), étagère à fichiers, points webcam / micro,
  volume affiché quand il change.
- **Le dock** : grossissement au survol, aperçus des fenêtres, dossiers d'applis, icônes d'arrière-plan,
  recherche, et la réduction des fenêtres façon macOS (effet Génie).
- **Profils** (Perso, Gaming, Stage, Études…) : apps du dock, couleur et fond d'écran changés en un clic.
- **Mode Gaming** automatique : l'île se fait discrète et le dock se cache pendant un jeu plein écran.
- **Palette de commandes** (Ctrl+Espace) : apps, dossiers, calculs (« 15% de 80 »), minuteur (« timer 10 min »).
- **Thèmes `.bloomtheme`** à s'échanger entre potes (double-clic sur le fichier pour l'installer).
- **Discord** : qui parle dans ton salon vocal, directement dans l'île (il faut être ajouté comme testeur
  de l'appli Discord : demande à Louis).

## Sécurité

- Si l'app se ferme, plante ou est arrêtée, la barre des tâches Windows revient toujours.
- Rien n'est envoyé nulle part, sauf : la météo (Open-Meteo), la vérification des mises à jour (GitHub) et,
  si tu l'actives, l'accès local à Discord.
