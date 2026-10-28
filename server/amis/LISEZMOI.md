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

## Créer le serveur (une seule fois, ≈ 5 minutes)

1. Crée un compte gratuit sur <https://dash.cloudflare.com/>.
2. **Storage & Databases → D1 → Create database**, nom : `opaline-amis`.
3. **Workers & Pages → Create → Worker** → **Deploy** (le « Hello World »), puis **Edit code** : efface tout, colle le
   contenu de [`worker.js`](worker.js) (Opaline le copie pour toi : Paramètres → Connecté → Amis en ligne → « Copier le
   code du serveur ») → **Deploy**.
4. Dans ce Worker : **Settings → Bindings → Add → D1 database** : nom de variable `DB`, base `opaline-amis` → **Deploy**.
5. Copie l'adresse du Worker (`https://….workers.dev`), colle-la dans Opaline (Paramètres → Connecté → Amis en ligne) et
   **Enregistrer**. Donne la même adresse à tes amis (« Copier l'invitation » envoie l'adresse et ton code).

Le serveur crée ses tables tout seul. Les offres gratuites de Cloudflare (Workers : 100 000 requêtes par jour ; D1 :
100 000 écritures par jour) suffisent pour plusieurs dizaines d'amis (chaque PC fait environ 1 200 requêtes par jour).

## Essayer sans Cloudflare

Avec Node.js 22.5 ou plus récent (SQLite intégré, rien à télécharger) :

```bash
node test-local.mjs            # scénario complet : deux PC, demande d'ami, présence, invisible, retrait, oubli
node test-local.mjs --serve 8799   # serveur d'essai sur http://127.0.0.1:8799 (adresse à coller dans Opaline)
```
