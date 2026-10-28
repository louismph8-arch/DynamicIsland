# Opaline (ex-Dynamic Island) pour Windows

Une île en verre d'opale en haut de l'écran, un dock façon macOS, une télécommande sur ton téléphone, des widgets
sur le bureau et plein de petits outils autour. Pour Windows 10 (2004+) et Windows 11, 64 bits. En français et en
anglais.

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
l'app : barre des tâches, fond d'écran, couleur d'accent, thème clair / sombre, curseur et règle du pare-feu de la
télécommande. L'assistant de désinstallation te demande ensuite si tu veux aussi effacer tes réglages.

## Ce qu'elle fait

- **L'île** : heure, musique (pochette, égaliseur, volume de l'appli qui joue), calendrier et minuteur /
  Pomodoro, contrôles (volume, luminosité, mode sombre, profils), widgets (météo, CPU, RAM, GPU, réseau),
  étagère à fichiers, points webcam / micro, volume affiché quand il change.
  - **Annonces** quand tu branches un casque, une manette ou une clé USB (avec la batterie si Windows la donne),
    et quand un téléchargement est terminé : fais glisser le fichier depuis l'île.
  - **Horloges du monde** (jusqu'à 4 villes).
  - **Santé du PC** : processeur, mémoire, GPU et sa température (cartes NVIDIA), espace des disques, applis
    les plus gourmandes (avec un bouton pour les fermer) ; alerte quand un disque passe sous 10 %.
  - **Amis en ligne** : lesquels de tes amis sont en ligne et à quoi ils jouent (voir plus bas).
  - **Thèmes saisonniers** : Halloween, Noël, Nouvel An, Saint-Valentin… et quelques surprises cachées.
- **Le dock** : grossissement au survol, aperçus des fenêtres, dossiers d'applis, icônes d'arrière-plan,
  recherche, réduction des fenêtres façon macOS (effet Génie), icônes harmonisées ou à ta couleur.
  - **Zone d'état** à droite : langue du clavier, mises à jour Windows (jamais installées toutes seules),
    corbeille (glisse un fichier dessus), pile Téléchargements.
  - **Bouton rapide** pour revenir à la barre des tâches Windows (clic droit sur le dock ou sur l'icône Opaline).
- **Télécommande sur ton téléphone** (Paramètres → Connecté, scanne le QR code) : musique, volume, micro et
  casque (ceux de Discord quand il est ouvert), profils, lancer tes applis, ouvrir un lien sur le PC, envoyer des
  photos et fichiers vers le PC, presse-papiers, pavé tactile, clavier, aperçu de l'écran, fenêtres, raccourcis,
  luminosité, thème, minuteur, message dans l'île, veille / redémarrer / éteindre, infos pour allumer le PC à
  distance (Wake on LAN).
- **Où tu veux** : fais glisser l'île ou le fond du dock vers un autre écran, en haut ou en bas.
- **Design** : verre liquide qui suit la souris, couleurs tirées de ton fond d'écran (option), thème clair /
  sombre au lever et au coucher du soleil (option), curseur de souris assorti (option).
- **Éclairage RGB** (option) : tes appareils Logitech (par G HUB) et ceux d'[OpenRGB](https://openrgb.org)
  prennent la couleur d'Opaline et changent avec tes profils.
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
  Claude). Deux moteurs au choix (Paramètres → Île → Claude) :
  - [Claude Code](https://claude.com/product/claude-code) installé, à jour et connecté à ton compte Claude :
    chaque question compte dans les limites de ton forfait ;
  - ou une **clé API Anthropic** (payée à l'usage par son propriétaire), pour ceux qui n'ont pas Claude Code.
- **Amis en ligne** (option) : par un petit serveur gratuit sur Cloudflare, que l'un de vous crée une fois pour
  tout le groupe (guide pas à pas dans Paramètres → Connecté → Amis en ligne).
- **Thèmes `.opaline`** à s'échanger entre potes (double-clic sur le fichier pour l'installer ; les anciens
  `.bloomtheme` marchent toujours).
- **Discord** : qui parle dans ton salon vocal, directement dans l'île (il faut être ajouté comme testeur de
  l'appli Discord : demande à Louis).

## En cas de souci

- **Mode sans échec** : garde **Maj** enfoncée en lançant Opaline (réglages par défaut, options coupées).
- Paramètres → À propos → **Exporter un rapport de bug** (sans données personnelles) : envoie-le à Louis.
- Les réglages sont sauvegardés automatiquement : Paramètres → À propos → revenir à une version précédente.
- Si un morceau de l'app plante, il s'arrête tout seul et le reste continue.

## Sécurité

- Si l'app se ferme, plante ou est arrêtée, la barre des tâches Windows revient toujours.
- Tout ce qui change Windows (fond d'écran, couleur d'accent, thème, curseur, barre des tâches, pare-feu, carte
  réseau) est désactivé par défaut, demande ton accord, et se remet comme avant : en décochant l'option, avec le
  bouton « Remettre comme avant » (carte réseau), ou en désinstallant l'app.
- **Télécommande** : désactivée par défaut, seulement sur ton réseau local « Privé » (jamais sur un Wi-Fi public),
  reliée par un code à usage unique ; chaque téléphone a sa clé, et « Contrôle complet » (souris, clavier, écran,
  arrêt) est à activer téléphone par téléphone.
- **Amis en ligne** : seulement entre amis qui se sont ajoutés tous les deux ; seuls « en ligne » et « joue à … »
  sont partagés, effacés après 10 minutes sans nouvelles ; mode invisible.
- **Clé API** du mini Claude : chiffrée par Windows sur ton PC, envoyée seulement à Anthropic.
- Rien n'est envoyé nulle part, sauf :
  - la météo et la recherche des villes des horloges (Open-Meteo) ;
  - la vérification des mises à jour (GitHub) ;
  - si tu l'actives, l'accès à Discord ;
  - si tu l'actives, ta présence au serveur des amis (le tien ou celui de ton groupe) ;
  - **tes questions au mini Claude**, et seulement quand tu en poses une : envoyées à Anthropic (par Claude Code
    avec ton compte Claude, ou avec ta clé API). Les derniers échanges de la conversation sont renvoyés avec chaque
    nouvelle question pour que Claude suive. Claude n'a accès à aucun fichier ni à aucune commande, et la
    conversation n'est pas enregistrée par l'app.
