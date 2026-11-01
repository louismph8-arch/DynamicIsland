# Serveur « Amis en ligne » d'Opaline (6D-4)

Un tout petit serveur, **gratuit**, sur **ton** compte Cloudflare : il dit à tes amis (qui ont Opaline) si tu es en ligne
et à quoi tu joues. Un seul serveur suffit pour tout ton groupe d'amis.

## Vie privée

- Chaque PC reçoit un **code d'ami** (« ABCD-EFGH ») et un secret, gardé chiffré par Windows sur le PC (le serveur n'en
  garde que l'empreinte).
- On ne voit que ses **amis mutuels** : chacun doit avoir ajouté le code de l'autre.
- Seuls « en ligne », « en jeu » et (si tu le choisis) le nom du jeu sont partagés. La présence s'efface d'elle-même
  10 minutes après les dernières nouvelles ; le mode **invisible** l'efface tout de suite.
- Pas d'adresse IP, pas d'historique. « Quitter les amis en ligne » efface ton code, ton pseudo et tes liens d'amis.
- **Bloquer** (version 3) : la personne ne voit plus ta présence, ne peut plus te redemander en ami (pour elle, ton code
  « n'existe pas ») et ne peut plus t'envoyer de message. Débloquer (ou rajouter son code) annule le blocage.
- **Messages rapides** (version 3) : seulement entre amis mutuels, choisis dans une liste fermée (« On joue ? », « Tu es
  dispo ? », « Rejoins-moi ! », « Plus tard ! ») — jamais de texte libre. Seul l'identifiant du message est gardé, 10 au plus
  en attente par personne, effacés dès qu'ils sont lus (ou après 24 h) ; 20 envois par heure au plus.

## Mettre à jour le serveur (version 3)

Le blocage, les messages rapides et « joue à … depuis … » demandent la version 3 du serveur. Une seule personne (celle qui a
créé le serveur) le fait : Cloudflare → **Workers & Pages** → ton Worker → **Edit code** → efface tout, colle le nouveau
`worker.js` (Opaline : Paramètres → Amis → « Copier le code du serveur ») → **Deploy**. La base est mise à jour toute seule,
rien n'est perdu (amis, pseudos, codes). Les anciennes versions d'Opaline continuent de marcher avec la version 3 ; avec un
serveur plus ancien, Opaline cache simplement les boutons « Bloquer » et « Message ».

## Créer le serveur (une seule fois, ≈ 5 minutes)

1. Crée un compte gratuit sur <https://dash.cloudflare.com/>.
2. **Storage & Databases → D1 → Create database**, nom : `opaline-amis`.
3. **Workers & Pages → Create → Worker** → **Deploy** (le « Hello World »), puis **Edit code** : efface tout, colle le
   contenu de [`worker.js`](worker.js) (Opaline le copie pour toi : Paramètres → Amis → « Copier le
   code du serveur ») → **Deploy**.
4. Dans ce Worker : **Settings → Bindings → Add → D1 database** : nom de variable `DB`, base `opaline-amis` → **Deploy**.
5. Copie l'adresse du Worker (`https://….workers.dev`), colle-la dans Opaline (Paramètres → Amis) et
   **Enregistrer**. Donne la même adresse à tes amis (« Copier l'invitation » envoie l'adresse et ton code).

### Recevoir les avis et les bugs dans Discord (facultatif)

« Donner mon avis » dans Opaline envoie le message (+ capture et rapport de bug si la personne les coche) à ce serveur, qui le
poste dans un salon de **ton** serveur Discord. L'adresse du salon reste secrète sur Cloudflare.

1. Discord : **Paramètres du serveur → Intégrations → Webhooks → Nouveau webhook**, choisis le salon (ex. `#opaline-avis`),
   puis **Copier l'URL du webhook**.
2. Cloudflare, dans le Worker : **Settings → Variables and Secrets → Add** : type **Secret**, nom `FEEDBACK_WEBHOOK`, valeur =
   l'URL copiée → **Deploy**.
3. Mets à jour le code du Worker (version 2 ou plus : **Edit code**, colle le nouveau `worker.js` → **Deploy**).

Seuls les PC inscrits sur ton serveur peuvent envoyer (5 avis par heure chacun), et les mentions (`@everyone`…) sont
désactivées. Sans le secret, le bouton explique que les avis ne sont pas encore activés.

Le serveur crée ses tables tout seul. Les offres gratuites de Cloudflare (Workers : 100 000 requêtes par jour ; D1 :
100 000 écritures par jour) suffisent pour plusieurs dizaines d'amis (chaque PC fait environ 1 200 requêtes par jour).

## Essayer sans Cloudflare

Avec Node.js 22.5 ou plus récent (SQLite intégré, rien à télécharger) :

```bash
node test-local.mjs            # scénario complet : deux PC, demande d'ami, présence, invisible, messages, blocage, retrait, oubli
node test-local.mjs --serve 8799   # serveur d'essai sur http://127.0.0.1:8799 (adresse à coller dans Opaline)
```
