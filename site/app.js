// Opaline — page de présentation : langue FR / EN, carrousel, dernière version lue sur GitHub (côté visiteur).
// Aucune dépendance, aucun cookie, aucun traceur. Seul appel réseau : l'API publique de GitHub.
(function () {
  "use strict";

  var REPO = "louismph8-arch/DynamicIsland";

  var T = {
    fr: {
      "doc.title": "Opaline pour Windows",
      "skip": "Aller au contenu",
      "nav.features": "Fonctionnalités", "nav.safety": "Sécurité", "nav.download": "Télécharger",
      "logo.alt": "Logo d'Opaline : un anneau d'opale sur une tuile sombre",
      "hero.tagline": "Une île en verre d'opale en haut de l'écran, un dock façon macOS et plein de petits outils autour. Pour Windows.",
      "hero.download": "Télécharger pour Windows", "hero.source": "Code source",
      "rel.fallback": "Dernière version sur GitHub · gratuit · open source (MIT)",
      "rel.info": "Version {v} · {size} · {date} · gratuit · open source (MIT)",
      "demo.title": "Aperçu de l'île", "demo.carousel": "carrousel", "demo.choose": "Choisir une image",
      "demo.pause": "Mettre en pause", "demo.play": "Relancer le défilement",
      "demo.s0": "Musique : pochette, égaliseur, volume de l'appli qui joue",
      "demo.s0.alt": "L'île ouverte sur la musique : pochette, titre, lecture et volume de l'appli",
      "demo.s1": "Notes rapides, cases à cocher comprises",
      "demo.s1.alt": "Onglet Notes de l'île avec une liste de courses cochable",
      "demo.s2": "Presse-papiers : historique et coffre chiffré",
      "demo.s2.alt": "Historique du presse-papiers avec filtres et proposition de garder un mot de passe dans le coffre",
      "demo.s3": "Santé du PC en un coup d'œil",
      "demo.s3.alt": "Santé du PC : processeur, mémoire, GPU, température et applis les plus gourmandes",
      "demo.s4": "Tes amis en ligne et ce à quoi ils jouent",
      "demo.s4.alt": "Onglet Amis : qui est en ligne et à quoi il joue",
      "demo.s5": "Palette de commandes (Ctrl + Espace)",
      "demo.s5.alt": "Palette de commandes : applis, calculs, minuteur, profils et question à Claude",
      "dock.alt": "Le dock d'Opaline en barre au bas de l'écran : recherche, applis, zone d'état",
      "dock.cap": "Le dock : grossissement, aperçus des fenêtres, dossiers, zone d'état",
      "f.title": "Ce qu'elle fait", "f.lead": "Tout est optionnel : active seulement ce qui te sert.",
      "f.island": "L'île", "f.island.t": "Heure, musique, calendrier, minuteur et Pomodoro, contrôles (volume, luminosité, mode sombre), météo, étagère à fichiers, horloges du monde.",
      "f.dock": "Le dock", "f.dock.t": "Façon macOS : grossissement, aperçus des fenêtres, dossiers d'applis, recherche, effet Génie, corbeille et téléchargements.",
      "f.clip": "Presse-papiers + coffre", "f.clip.t": "Historique de ce que tu copies (textes, liens, images), épingles, et un coffre chiffré par Windows pour tes identifiants.",
      "f.notes": "Notes", "f.notes.t": "Des notes rapides dans l'île, avec cases à cocher, couleurs et épingles ; chiffrées si tu veux.",
      "f.stage": "Stage Manager", "f.stage.t": "La fenêtre active au centre, les autres en miniatures vivantes sur le côté ; un clic pour échanger.",
      "f.opal": "Verre opalin", "f.opal.t": "Un style unique : verre laiteux teinté de ta couleur, reflets irisés qui suivent la souris, balayage nacré. Trois intensités au choix.",
      "f.focus": "Concentration", "f.focus.t": "Ne pas déranger, veilleuse contre la lumière bleue et résumé de ta semaine (temps d'écran, jeu, Pomodoros).",
      "f.widgets": "Widgets des potes", "f.widgets.t": "Horloge, météo, musique, stats sur le bureau, et des widgets créés par tes amis, sans code à exécuter.",
      "f.remote": "Télécommande", "f.remote.t": "Depuis ton téléphone, sur ton réseau local : musique, volume, micro, applis, pavé tactile, clavier, envoi de fichiers.",
      "f.friends": "Amis en ligne", "f.friends.t": "Vois qui a Opaline et à quoi il joue, avec codes d'ami mutuels et mode invisible. Désactivé par défaut.",
      "f.ai": "Assistant IA", "f.ai.t": "Pose une question à Claude depuis l'île ou la palette, avec ton forfait Claude Code ou ta propre clé API.",
      "f.themes": "Thèmes et fonds", "f.themes.t": "Verre liquide, couleurs tirées du fond d'écran, fonds dessinés par l'app, fond animé, thèmes saisonniers, thèmes .opaline à s'échanger.",
      "f.auto": "Profils et automatisations", "f.auto.t": "Profils Perso, Gaming, Études… mode Gaming automatique et règles « Quand… alors… » (jeu lancé, heure, casque branché).",
      "g.remote": "Relier son téléphone : un QR code, un code à usage unique",
      "g.remote.alt": "Paramètres de la télécommande : QR code à scanner et téléphones reliés",
      "g.widget": "Un widget de pote sur le bureau", "g.widget.alt": "Widget de bureau horloge et météo",
      "g.stage": "Stage Manager", "g.stage.alt": "Colonne de Stage Manager avec trois groupes de fenêtres en miniature",
      "d.title": "Télécharger", "d.btn": "Télécharger Opaline-Setup.exe", "d.hash": "Empreinte SHA-256 :",
      "d.req": "Configuration requise",
      "d.req1": "Windows 11 ou Windows 10 (version 2004 ou plus récente)",
      "d.req2": "PC 64 bits (x64)",
      "d.req3": "Pas besoin de droits administrateur ni d'installer .NET : tout est inclus",
      "d.steps": "Installer",
      "d.s1": "Télécharge Opaline-Setup.exe.",
      "d.s2": "(Conseillé) Vérifie son empreinte, voir plus bas.",
      "d.s3": "Lance-le. Si Windows affiche « Windows a protégé votre ordinateur », clique <b>Informations complémentaires</b> puis <b>Exécuter quand même</b>.",
      "d.s4": "Un petit assistant t'aide à choisir ton profil, ta couleur et ce que tu veux afficher.",
      "s.title": "Est-ce que c'est sûr ?",
      "s.q1": "Le code est-il public ?",
      "s.a1": "Oui. Tout le code est sur <a href=\"https://github.com/louismph8-arch/DynamicIsland\">GitHub</a>, sous licence MIT : n'importe qui peut lire exactement ce que fait l'app, et la compiler soi-même.",
      "s.q2": "Comment vérifier l'installeur ?",
      "s.a2": "<p>Chaque version publie l'empreinte <b>SHA-256</b> de <code>Opaline-Setup.exe</code> (dans les notes de la version, dans le fichier <code>Opaline-Setup.exe.sha256</code> et à côté du fichier sur GitHub).</p><ol><li>Dans le dossier du fichier téléchargé, ouvre PowerShell et tape <code>Get-FileHash .\\Opaline-Setup.exe</code> : l'empreinte doit être <b>exactement</b> la même.</li><li>Ouvre le lien <b>VirusTotal</b> des notes de la version (ou dépose le fichier sur <a href=\"https://www.virustotal.com\">virustotal.com</a>) : environ 70 antivirus l'analysent. 0 ou 1 détection « générique » sur un fichier non signé est courant ; plusieurs détections nommées = ne l'installe pas.</li></ol>",
      "s.q3": "Pourquoi Windows avertit-il (SmartScreen) ?",
      "s.a3": "L'installeur n'est pas signé : un certificat coûte de l'argent chaque année, et même signé, Windows avertit tant que le fichier n'a pas été téléchargé par beaucoup de monde. L'avertissement dit seulement « éditeur inconnu », pas « virus ». Pour continuer : <b>Informations complémentaires</b> → <b>Exécuter quand même</b>.",
      "s.q4": "Qu'est-ce que l'app ne fait jamais ?",
      "s.a4": "Pas d'injection dans d'autres programmes, pas de lecture du clavier en continu, pas de pub, pas de pistage. Elle suit les fenêtres ouvertes (pour le dock) avec l'API officielle de Windows et n'utilise les droits administrateur que pour 2 choses, toujours après ta confirmation : la règle du pare-feu de la télécommande et la préparation du réveil à distance. Tout ce qui change Windows est désactivé par défaut et se remet comme avant.",
      "s.q5": "Qu'est-ce qui part sur Internet ?",
      "s.a5": "<ul><li>La météo et la recherche des villes (Open-Meteo).</li><li>La vérification des mises à jour (GitHub).</li><li>Seulement si tu les actives : Discord (qui parle dans ton salon), ta présence au serveur des amis, tes questions à l'assistant IA (à Anthropic, quand tu en poses une).</li></ul><p>La télécommande reste sur ton réseau local « Privé », reliée par un code à usage unique.</p>",
      "s.q6": "Comment la désinstaller ?",
      "s.a6": "Paramètres Windows → Applications → <b>Opaline</b> → Désinstaller. Windows revient comme avant : barre des tâches, fond d'écran, couleur d'accent, thème, curseur et règle du pare-feu. L'assistant te demande ensuite si tu veux aussi effacer tes réglages.",
      "s.q7": "Et si quelque chose ne va pas ?",
      "s.a7": "Garde <b>Maj</b> enfoncée en lançant Opaline pour le mode sans échec. Si un morceau de l'app plante, il s'arrête tout seul et le reste continue ; la barre des tâches revient toujours. Paramètres → À propos → <b>Donner mon avis / Signaler un bug</b>, ou <b>Exporter un rapport de bug</b> (sans données personnelles).",
      "l.title": "Liens", "l.source": "Code source", "l.notes": "Notes de version", "l.bug": "Signaler un bug", "l.license": "Licence MIT",
      "foot.claude": "Fait avec Claude",
      "foot.privacy": "Cette page n'utilise ni cookies, ni traceurs, ni polices externes.",
      "unit.mb": "Mo"
    },
    en: {
      "doc.title": "Opaline for Windows",
      "skip": "Skip to content",
      "nav.features": "Features", "nav.safety": "Safety", "nav.download": "Download",
      "logo.alt": "Opaline logo: an opal ring on a dark tile",
      "hero.tagline": "An opal-glass island at the top of your screen, a macOS-style dock and lots of handy little tools around them. For Windows.",
      "hero.download": "Download for Windows", "hero.source": "Source code",
      "rel.fallback": "Latest version on GitHub · free · open source (MIT)",
      "rel.info": "Version {v} · {size} · {date} · free · open source (MIT)",
      "demo.title": "A look at the island", "demo.carousel": "carousel", "demo.choose": "Choose a picture",
      "demo.pause": "Pause", "demo.play": "Resume the slideshow",
      "demo.s0": "Music: artwork, equalizer, volume of the app that's playing",
      "demo.s0.alt": "The island open on music: artwork, title, playback and app volume",
      "demo.s1": "Quick notes, checkboxes included",
      "demo.s1.alt": "The island's Notes tab with a grocery checklist",
      "demo.s2": "Clipboard: history and encrypted vault",
      "demo.s2.alt": "Clipboard history with filters and an offer to keep a password in the vault",
      "demo.s3": "Your PC's health at a glance",
      "demo.s3.alt": "PC health: CPU, memory, GPU, temperature and the hungriest apps",
      "demo.s4": "Friends online and what they're playing",
      "demo.s4.alt": "Friends tab: who is online and what they are playing",
      "demo.s5": "Command palette (Ctrl + Space)",
      "demo.s5.alt": "Command palette: apps, math, timer, profiles and a question for Claude",
      "dock.alt": "Opaline's dock as a bar at the bottom of the screen: search, apps, status area",
      "dock.cap": "The dock: magnification, window previews, folders, status area",
      "f.title": "What it does", "f.lead": "Everything is optional: turn on only what you need.",
      "f.island": "The island", "f.island.t": "Clock, music, calendar, timer and Pomodoro, controls (volume, brightness, dark mode), weather, file shelf, world clocks.",
      "f.dock": "The dock", "f.dock.t": "macOS-style: magnification, window previews, app folders, search, Genie effect, recycle bin and downloads.",
      "f.clip": "Clipboard + vault", "f.clip.t": "History of what you copy (text, links, images), pins, and a vault encrypted by Windows for your logins.",
      "f.notes": "Notes", "f.notes.t": "Quick notes in the island, with checkboxes, colors and pins; encrypted if you like.",
      "f.stage": "Stage Manager", "f.stage.t": "The active window in the middle, the others as live thumbnails on the side; one click to swap.",
      "f.opal": "Opal glass", "f.opal.t": "A unique look: milky glass tinted with your color, iridescent reflections that follow the mouse, a pearly sweep. Three intensities to choose from.",
      "f.focus": "Focus", "f.focus.t": "Do not disturb, night light against blue light and a summary of your week (screen time, gaming, Pomodoros).",
      "f.widgets": "Friends' widgets", "f.widgets.t": "Clock, weather, music, stats on the desktop, plus widgets made by your friends, with no code to run.",
      "f.remote": "Remote", "f.remote.t": "From your phone, on your local network: music, volume, mic, apps, touchpad, keyboard, file transfer.",
      "f.friends": "Friends online", "f.friends.t": "See who has Opaline and what they're playing, with mutual friend codes and invisible mode. Off by default.",
      "f.ai": "AI assistant", "f.ai.t": "Ask Claude a question from the island or the palette, with your Claude Code plan or your own API key.",
      "f.themes": "Themes and wallpapers", "f.themes.t": "Liquid glass, colors taken from your wallpaper, wallpapers drawn by the app, animated wallpaper, seasonal themes, .opaline themes to share.",
      "f.auto": "Profiles and automations", "f.auto.t": "Personal, Gaming, Study profiles… automatic Gaming mode and “When… then…” rules (game started, time of day, headset plugged in).",
      "g.remote": "Link your phone: a QR code, a one-time code",
      "g.remote.alt": "Remote settings: QR code to scan and linked phones",
      "g.widget": "A friend's widget on the desktop", "g.widget.alt": "Clock and weather desktop widget",
      "g.stage": "Stage Manager", "g.stage.alt": "Stage Manager column with three groups of window thumbnails",
      "d.title": "Download", "d.btn": "Download Opaline-Setup.exe", "d.hash": "SHA-256 hash:",
      "d.req": "Requirements",
      "d.req1": "Windows 11 or Windows 10 (version 2004 or later)",
      "d.req2": "64-bit PC (x64)",
      "d.req3": "No admin rights needed and no .NET to install: everything is included",
      "d.steps": "Install",
      "d.s1": "Download Opaline-Setup.exe.",
      "d.s2": "(Recommended) Check its hash, see below.",
      "d.s3": "Run it. If Windows shows “Windows protected your PC”, click <b>More info</b> then <b>Run anyway</b>.",
      "d.s4": "A short setup assistant helps you pick your profile, your color and what to show.",
      "s.title": "Is it safe?",
      "s.q1": "Is the code public?",
      "s.a1": "Yes. All the code is on <a href=\"https://github.com/louismph8-arch/DynamicIsland\">GitHub</a> under the MIT license: anyone can read exactly what the app does, and build it themselves.",
      "s.q2": "How do I check the installer?",
      "s.a2": "<p>Every release publishes the <b>SHA-256</b> hash of <code>Opaline-Setup.exe</code> (in the release notes, in the <code>Opaline-Setup.exe.sha256</code> file and next to the file on GitHub).</p><ol><li>In the folder of the downloaded file, open PowerShell and type <code>Get-FileHash .\\Opaline-Setup.exe</code>: the hash must be <b>exactly</b> the same.</li><li>Open the <b>VirusTotal</b> link in the release notes (or drop the file on <a href=\"https://www.virustotal.com\">virustotal.com</a>): about 70 antivirus engines scan it. 0 or 1 “generic” detection on an unsigned file is common; several named detections = don't install it.</li></ol>",
      "s.q3": "Why does Windows warn me (SmartScreen)?",
      "s.a3": "The installer isn't signed: a certificate costs money every year, and even when signed, Windows warns until the file has been downloaded by many people. The warning only says “unknown publisher”, not “virus”. To continue: <b>More info</b> → <b>Run anyway</b>.",
      "s.q4": "What does the app never do?",
      "s.a4": "No injection into other programs, no continuous keyboard reading, no ads, no tracking. It follows open windows (for the dock) with Windows' official API and only uses admin rights for 2 things, always after you confirm: the firewall rule for the remote and the wake-on-LAN setup. Everything that changes Windows is off by default and goes back to how it was.",
      "s.q5": "What is sent over the Internet?",
      "s.a5": "<ul><li>Weather and city search (Open-Meteo).</li><li>Update checks (GitHub).</li><li>Only if you turn them on: Discord (who is talking in your channel), your presence on the friends server, your questions to the AI assistant (to Anthropic, only when you ask one).</li></ul><p>The remote stays on your “Private” local network and is linked with a one-time code.</p>",
      "s.q6": "How do I uninstall it?",
      "s.a6": "Windows Settings → Apps → <b>Opaline</b> → Uninstall. Windows goes back to how it was: taskbar, wallpaper, accent color, theme, cursor and firewall rule. The uninstaller then asks whether you also want to delete your settings.",
      "s.q7": "What if something goes wrong?",
      "s.a7": "Hold <b>Shift</b> while starting Opaline for safe mode. If one part of the app crashes, it stops on its own and the rest keeps going; the taskbar always comes back. Settings → About → <b>Give feedback / Report a bug</b>, or <b>Export a bug report</b> (no personal data).",
      "l.title": "Links", "l.source": "Source code", "l.notes": "Release notes", "l.bug": "Report a bug", "l.license": "MIT license",
      "foot.claude": "Made with Claude",
      "foot.privacy": "This page uses no cookies, no trackers and no external fonts.",
      "unit.mb": "MB"
    }
  };

  var lang = "fr";
  var release = null;

  function store(key, value) {
    try { if (value === undefined) return localStorage.getItem(key); localStorage.setItem(key, value); } catch (e) { return null; }
    return null;
  }

  function pickLang() {
    var q = /[?&]lang=(fr|en)\b/.exec(location.search);
    if (q) return q[1];
    var saved = store("opaline-lang");
    if (saved === "fr" || saved === "en") return saved;
    var list = navigator.languages && navigator.languages.length ? navigator.languages : [navigator.language || "fr"];
    for (var i = 0; i < list.length; i++) {
      var l = String(list[i]).toLowerCase();
      if (l.indexOf("fr") === 0) return "fr";
      if (l.indexOf("en") === 0) return "en";
    }
    return "en";
  }

  function t(key) { return (T[lang] && T[lang][key]) || T.fr[key] || ""; }

  function each(sel, fn) { Array.prototype.forEach.call(document.querySelectorAll(sel), fn); }

  function applyLang(next) {
    lang = next;
    document.documentElement.lang = lang;
    document.title = t("doc.title");
    each("[data-i18n]", function (el) { el.textContent = t(el.getAttribute("data-i18n")); });
    // Textes avec un peu de mise en forme : ils viennent uniquement de l'objet T ci-dessus, jamais du réseau.
    each("[data-i18n-html]", function (el) { el.innerHTML = t(el.getAttribute("data-i18n-html")); });
    each("[data-i18n-alt]", function (el) { el.alt = t(el.getAttribute("data-i18n-alt")); });
    each("[data-i18n-aria]", function (el) {
      var k = el.getAttribute("data-i18n-aria");
      if (k === "demo.carousel") el.setAttribute("aria-roledescription", t(k));
      else el.setAttribute("aria-label", t(k));
    });
    each("img[data-img]", function (el) { el.src = "img/" + lang + "/" + el.getAttribute("data-img") + ".webp"; });
    each(".lang button", function (b) { b.setAttribute("aria-pressed", String(b.getAttribute("data-lang") === lang)); });
    updatePauseLabel();
    showRelease();
  }

  // ---------- Carrousel ----------
  var slides = [], dots = [], current = 0, timer = null, paused = false, pauseBtn = null;
  var reduced = window.matchMedia && window.matchMedia("(prefers-reduced-motion: reduce)").matches;

  function go(i) {
    if (!slides.length) return;
    current = (i + slides.length) % slides.length;
    slides.forEach(function (s, n) {
      var on = n === current;
      s.classList.toggle("is-on", on);
      s.setAttribute("aria-hidden", String(!on));
    });
    dots.forEach(function (d, n) { if (n === current) d.setAttribute("aria-current", "true"); else d.removeAttribute("aria-current"); });
  }
  function start() { stop(); if (!paused && !reduced) timer = setInterval(function () { go(current + 1); }, 4200); }
  function stop() { if (timer) { clearInterval(timer); timer = null; } }
  function updatePauseLabel() {
    if (!pauseBtn) return;
    var isPaused = paused || reduced;
    pauseBtn.setAttribute("aria-pressed", String(isPaused));
    pauseBtn.setAttribute("aria-label", t(isPaused ? "demo.play" : "demo.pause"));
  }

  function setupCarousel() {
    var root = document.getElementById("carousel");
    if (!root) return;
    slides = Array.prototype.slice.call(root.querySelectorAll(".slide"));
    dots = Array.prototype.slice.call(root.querySelectorAll(".dots button:not(.pause)"));
    pauseBtn = root.querySelector(".dots .pause");
    dots.forEach(function (d, n) { d.addEventListener("click", function () { go(n); start(); }); });
    if (pauseBtn) pauseBtn.addEventListener("click", function () {
      if (reduced) { reduced = false; paused = false; } else { paused = !paused; }
      updatePauseLabel(); start();
    });
    // Pas de défilement pendant que la souris ou le focus clavier est dessus
    root.addEventListener("mouseenter", stop);
    root.addEventListener("mouseleave", start);
    root.addEventListener("focusin", stop);
    root.addEventListener("focusout", start);
    document.addEventListener("visibilitychange", function () { if (document.hidden) stop(); else start(); });
    go(0);
    updatePauseLabel();
    start();
  }

  // ---------- Dernière version (API publique de GitHub, côté visiteur) ----------
  function formatSize(bytes) {
    var mb = bytes / (1024 * 1024);
    return mb.toLocaleString(lang === "fr" ? "fr-FR" : "en-US", { maximumFractionDigits: 1 }) + " " + t("unit.mb");
  }
  function formatDate(iso) {
    try {
      return new Date(iso).toLocaleDateString(lang === "fr" ? "fr-FR" : "en-US", { day: "numeric", month: "long", year: "numeric" });
    } catch (e) { return ""; }
  }

  function showRelease() {
    if (!release) return;
    var text = t("rel.info")
      .replace("{v}", release.version)
      .replace("{size}", release.size ? formatSize(release.size) : "")
      .replace("{date}", release.date ? formatDate(release.date) : "")
      .split(" · ").filter(function (p) { return p.trim(); }).join(" · ");
    each(".rel-info", function (el) { el.textContent = text; });
    var hash = document.querySelector(".hash");
    if (hash && release.sha256) {
      hash.querySelector(".hash-val").textContent = release.sha256;
      hash.hidden = false;
    }
  }

  function safeGithubUrl(u) {
    return typeof u === "string" && /^https:\/\/github\.com\/louismph8-arch\/DynamicIsland\/releases\/download\//.test(u) ? u : null;
  }

  function loadRelease() {
    if (!window.fetch) return;
    var ctrl = window.AbortController ? new AbortController() : null;
    var to = setTimeout(function () { if (ctrl) ctrl.abort(); }, 8000);
    fetch("https://api.github.com/repos/" + REPO + "/releases/latest", {
      headers: { "Accept": "application/vnd.github+json" },
      credentials: "omit", referrerPolicy: "no-referrer", signal: ctrl ? ctrl.signal : undefined
    }).then(function (r) { if (!r.ok) throw new Error(r.status); return r.json(); })
      .then(function (data) {
        clearTimeout(to);
        var assets = Array.isArray(data.assets) ? data.assets : [];
        var exe = null;
        for (var i = 0; i < assets.length; i++) {
          var n = String(assets[i].name || "");
          if (/\.exe$/i.test(n) && /setup/i.test(n)) { exe = assets[i]; break; }
        }
        var tag = String(data.tag_name || "").replace(/^v/i, "");
        if (!/^\d+\.\d+\.\d+([-.+][0-9A-Za-z.-]+)?$/.test(tag)) return;
        var digest = exe && typeof exe.digest === "string" ? /^sha256:([0-9a-f]{64})$/i.exec(exe.digest) : null;
        release = {
          version: tag,
          size: exe && typeof exe.size === "number" ? exe.size : 0,
          date: data.published_at || "",
          sha256: digest ? digest[1].toLowerCase() : null
        };
        var direct = exe ? safeGithubUrl(exe.browser_download_url) : null;
        var main = document.getElementById("dl-main");
        if (main && direct) main.href = direct;
        showRelease();
      })
      .catch(function () { clearTimeout(to); /* le texte de repli reste affiché et les boutons visent la page de la dernière version */ });
  }

  document.addEventListener("DOMContentLoaded", function () {
    each(".lang button", function (b) {
      b.addEventListener("click", function () {
        var l = b.getAttribute("data-lang");
        store("opaline-lang", l);
        applyLang(l);
      });
    });
    setupCarousel();
    applyLang(pickLang());
    loadRelease();
  });
})();
