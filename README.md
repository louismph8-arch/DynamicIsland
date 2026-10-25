# Opaline (ex-Dynamic Island) pour Windows

Une île en verre d'opale en haut de l'écran, un dock façon macOS, des widgets sur le bureau et plein de petits
outils autour. Pour Windows 10 (2004+) et Windows 11, 64 bits.

## Installer

1. Télécharge **`Opaline-Setup.exe`** dans la [dernière version](../../releases/latest).
2. Lance-le. Windows peut afficher « Windows a protégé votre ordinateur » (l'installeur n'est pas signé) :
   clique **Informations complémentaires** puis **Exécuter quand même**.
3. Pas besoin de droits administrateur ni d'installer .NET : tout est inclus.

Au premier lancement, un petit assistant t'aide à choisir ton profil, ta couleur, ton fond d'écran et ce que
tu veux afficher. Tout se change ensuite dans les paramètres (clic sur l'icône Opaline près de l'heure).

L'app se met ensuite à jour toute seule : une nouvelle version s'annonce dans l'île, et elle ne s'installe que
si tu cliques « Installer ». Tu avais déjà **Dynamic Island** ? C'est la même app sous son nouveau nom : la
mise à jour garde tes réglages.

Pour la retirer : Paramètres Windows → Applications → **Opaline** → Désinstaller. Windows revient comme avant
l'app : barre des tâches, fond d'écran, couleur d'accent, thème clair / sombre et curseur. L'assistant de
désinstallation te demande ensuite si tu veux aussi effacer tes réglages.

## Ce qu'elle fait

- **L'île** : heure, musique (pochette, égaliseur, volume de l'appli qui joue), calendrier et minuteur /
  Pomodoro, contrôles (volume, luminosité, mode sombre, profils), widgets (météo, CPU, RAM, GPU, réseau),
  étagère à fichiers, points webcam / micro, volume affiché quand il change.
- **Le dock** : grossissement au survol, aperçus des fenêtres, dossiers d'applis, icônes d'arrière-plan,
  recherche, réduction des fenêtres façon macOS (effet Génie), icônes harmonisées ou à ta couleur.
- **Où tu veux** : fais glisser l'île ou le fond du dock vers un autre écran, en haut ou en bas.
- **Design** : verre liquide qui suit la souris, couleurs tirées de ton fond d'écran (option), thème clair /
  sombre au lever et au coucher du soleil (option), curseur de souris assorti (option).
- **Fonds d'écran** : 5 fonds dessinés par l'app, un fond qui change selon l'heure, un fond animé derrière les
  icônes du bureau (en pause pendant les jeux).
- **Widgets du bureau** : horloge géante, météo, musique, calendrier, stats du PC.
- **Barre de menus** en haut de l'écran (option).
- **Profils** (Perso, Gaming, Stage, Études…) : apps du dock, couleur et fond d'écran changés en un clic.
- **Mode Gaming** automatique : l'île se fait discrète et le dock se cache pendant un jeu plein écran ;
  **Ctrl + Alt + D** le fait apparaître quand même.
- **Palette de commandes** (Ctrl + Espace) : apps, dossiers, calculs (« 15% de 80 »), minuteur
  (« timer 10 min »), profils.
- **Mini Claude** : dans la palette, tape « ? » suivi de ta question ; la réponse s'écrit dans l'île (onglet
  Claude). Il faut [Claude Code](https://claude.com/product/claude-code) installé, à jour et connecté à ton
  compte Claude : chaque question compte dans les limites de ton forfait. Si Claude Code est trop ancien ou pas
  connecté, l'île te le dit et te propose un bouton pour régler ça.
- **Thèmes `.opaline`** à s'échanger entre potes (double-clic sur le fichier pour l'installer ; les anciens
  `.bloomtheme` marchent toujours).
- **Discord** : qui parle dans ton salon vocal, directement dans l'île (il faut être ajouté comme testeur de
  l'appli Discord : demande à Louis).

## Sécurité

- Si l'app se ferme, plante ou est arrêtée, la barre des tâches Windows revient toujours.
- Tout ce qui change Windows (fond d'écran, couleur d'accent, thème, curseur, barre des tâches) est désactivé par
  défaut, et revient comme avant quand tu décoches l'option ou que tu désinstalles l'app.
- Rien n'est envoyé nulle part, sauf :
  - la météo (Open-Meteo) ;
  - la vérification des mises à jour (GitHub) ;
  - si tu l'actives, l'accès à Discord ;
  - **tes questions au mini Claude**, et seulement quand tu en poses une : Claude Code les envoie à Anthropic
    avec ton compte Claude. Les derniers échanges de la conversation sont renvoyés avec chaque nouvelle question
    pour que Claude suive. Claude n'a accès à aucun fichier ni à aucune commande, et la conversation n'est pas
    enregistrée par l'app.
