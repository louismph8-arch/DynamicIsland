// Essai du serveur « Amis en ligne » SANS Cloudflare (Node.js 22.5+, SQLite intégré, rien à télécharger) :
//   node test-local.mjs            → scénario complet (deux PC, demande d'ami, présence, invisible, « depuis », messages
//                                    rapides, blocage, retrait, avis, oubli ; part d'une base de la version 2 à mettre à jour)
//   node test-local.mjs --serve 8799 → le serveur sur http://127.0.0.1:8799 pour essayer Opaline (Paramètres → Connecté)
// La base D1 de Cloudflare est imitée avec node:sqlite (mêmes requêtes SQL).

import { DatabaseSync } from "node:sqlite";
import http from "node:http";
import worker from "./worker.js";

class Statement {
  constructor(sqlite, sql, args = []) { this.sqlite = sqlite; this.sql = sql; this.args = args; }
  bind(...args) { return new Statement(this.sqlite, this.sql, args); }
  async run() { return this.runNow(); }
  runNow() { const r = this.sqlite.prepare(this.sql).run(...this.args); return { meta: { changes: Number(r.changes) } }; }
  async first() { return this.sqlite.prepare(this.sql).get(...this.args) ?? null; }
  async all() { return { results: this.sqlite.prepare(this.sql).all(...this.args) }; }
}

function d1() {
  const sqlite = new DatabaseSync(":memory:");
  return {
    exec: sql => sqlite.exec(sql),
    prepare: sql => new Statement(sqlite, sql),
    async batch(statements) {
      sqlite.exec("BEGIN");
      try {
        const results = statements.map(s => s.runNow());
        sqlite.exec("COMMIT");
        return results;
      } catch (error) {
        sqlite.exec("ROLLBACK");
        throw error;
      }
    },
  };
}

const env = { DB: d1() };
// Base créée par la version 2 du serveur (présence sans colonne « since ») : la version 3 doit la mettre à jour seule.
env.DB.exec("CREATE TABLE presence (id TEXT PRIMARY KEY, status TEXT NOT NULL, game TEXT, updated INTEGER NOT NULL)");

// Faux webhook Discord (7C) : rien n'est envoyé, la dernière requête est gardée pour les vérifications. Pour essayer avec un
// vrai salon : OPALINE_WEBHOOK=https://discord.com/api/webhooks/… node test-local.mjs --serve 8799
let lastWebhook = null;
const realFetch = globalThis.fetch;
if (process.env.OPALINE_WEBHOOK) env.FEEDBACK_WEBHOOK = process.env.OPALINE_WEBHOOK;
else {
  env.FEEDBACK_WEBHOOK = "https://discord.example/api/webhooks/essai";
  globalThis.fetch = async (url, init) => {
    if (String(url).startsWith("https://discord.example/")) {
      lastWebhook = { url: String(url), form: init.body };
      if (process.argv[2] === "--serve") console.log("Avis reçu (faux webhook) :", JSON.parse(init.body.get("payload_json")).embeds[0].description);
      return new Response(null, { status: 204 });
    }
    return realFetch(url, init);
  };
}

if (process.argv[2] === "--serve") {
  const port = Number(process.argv[3] || 8799);
  http.createServer(async (req, res) => {
    const chunks = [];
    for await (const chunk of req) chunks.push(chunk);
    const request = new Request(`http://127.0.0.1:${port}${req.url}`, {
      method: req.method,
      headers: req.headers,
      body: req.method === "GET" || req.method === "HEAD" ? undefined : Buffer.concat(chunks),
    });
    const response = await worker.fetch(request, env);
    res.writeHead(response.status, Object.fromEntries(response.headers));
    res.end(Buffer.from(await response.arrayBuffer()));
  }).listen(port, "127.0.0.1", () => console.log(`Serveur Amis en ligne (essai) : http://127.0.0.1:${port}`));
} else {
  await scenario();
}

async function call(method, path, auth, body) {
  const headers = { "Content-Type": "application/json" };
  if (auth) headers.Authorization = `Bearer ${auth.id}.${auth.secret}`;
  const response = await worker.fetch(new Request("https://amis.example" + path, { method, headers, body: body ? JSON.stringify(body) : undefined }), env);
  return { status: response.status, data: await response.json() };
}

async function scenario() {
  let failures = 0;
  const check = (ok, what) => { console.log(`${ok ? "OK" : "ÉCHEC"} : ${what}`); if (!ok) failures++; };

  check((await call("GET", "/")).data.app === "opaline-amis", "le serveur répond");
  const louis = (await call("POST", "/register", null, { name: "Louis" })).data;
  const max = (await call("POST", "/register", null, { name: "Max" })).data;
  check(/^[A-Z2-9]{8}$/.test(louis.id) && louis.secret.length >= 40 && louis.id !== max.id, "deux PC inscrits, codes d'ami différents");
  check((await call("GET", "/friends", { id: louis.id, secret: "faux-secret-de-vingt-caracteres" })).status === 401, "mauvais secret : refusé");

  await call("POST", "/me", louis, { status: "game", game: "Valorant" });
  await call("POST", "/me", max, { status: "online" });
  check((await call("GET", "/friends", max)).data.friends.length === 0, "pas encore amis : rien n'est visible");

  await call("POST", "/friends/add", louis, { code: max.id.slice(0, 4) + "-" + max.id.slice(4).toLowerCase() });
  let seen = (await call("GET", "/friends", max)).data;
  check(seen.friends.length === 0 && seen.incoming.length === 1 && seen.incoming[0].name === "Louis", "demande reçue par Max (Louis ne voit pas encore sa présence)");
  check((await call("GET", "/friends", louis)).data.outgoing[0].code === max.id, "demande en attente chez Louis");

  await call("POST", "/friends/add", max, { code: louis.id });
  seen = (await call("GET", "/friends", max)).data;
  check(seen.friends[0].name === "Louis" && seen.friends[0].status === "game" && seen.friends[0].game === "Valorant" && seen.incoming.length === 0,
    "amis mutuels : Max voit « Louis joue à Valorant »");

  await call("POST", "/me", louis, { status: "game", game: "Valorant", invisible: true });
  check((await call("GET", "/friends", max)).data.friends[0].status === "offline", "Louis invisible : hors ligne pour Max");

  await call("POST", "/me", louis, { status: "online" });
  check((await call("GET", "/friends", max)).data.friends[0].status === "online", "Louis visible de nouveau : en ligne");

  // « depuis » (version 3) : gardé tant que l'état ne change pas, remis à l'heure quand il change
  const info = (await call("GET", "/")).data;
  check(info.version >= 3 && info.features.includes("block") && info.messages.includes("play"), "version 3 : blocage et messages rapides annoncés");
  const since1 = (await call("GET", "/friends", max)).data.friends[0].since;
  await new Promise(resolve => setTimeout(resolve, 15));
  await call("POST", "/me", louis, { status: "online" });
  const since2 = (await call("GET", "/friends", max)).data.friends[0].since;
  await call("POST", "/me", louis, { status: "game", game: "Valorant" });
  const since3 = (await call("GET", "/friends", max)).data.friends[0].since;
  check(typeof since1 === "number" && since1 === since2 && since3 > since2, "« depuis » : même état = même heure, nouveau jeu = nouvelle heure (ancienne base mise à jour)");

  // Messages rapides (version 3)
  check((await call("POST", "/messages/send", max, { code: louis.id, message: "Viens sur mon serveur louche" })).status === 400,
    "message hors de la liste fermée : refusé");
  check((await call("POST", "/messages/send", max, { code: louis.id, message: "play" })).status === 200, "Max envoie « On joue ? » à Louis");
  let inbox = (await call("GET", "/messages", louis)).data.messages;
  check(inbox.length === 1 && inbox[0].code === max.id && inbox[0].name === "Max" && inbox[0].message === "play", "Louis reçoit le message de Max");
  check((await call("GET", "/messages", louis)).data.messages.length === 0, "message effacé dès qu'il est lu");
  const stranger = (await call("POST", "/register", null, { name: "Inconnu" })).data;
  check((await call("POST", "/messages/send", stranger, { code: louis.id, message: "play" })).status === 403, "pas ami : message refusé");
  let accepted = 1;
  for (let i = 0; i < 25; i++) if ((await call("POST", "/messages/send", max, { code: louis.id, message: "available" })).status === 200) accepted++;
  check(accepted === 20, `20 messages par heure au plus (${accepted} acceptés)`);
  inbox = (await call("GET", "/messages", louis)).data.messages;
  check(inbox.length === 10, `file courte : 10 messages en attente au plus (${inbox.length})`);

  // Blocage (version 3)
  await call("POST", "/friends/add", stranger, { code: louis.id });
  check((await call("GET", "/friends", louis)).data.incoming.some(f => f.code === stranger.id), "demande reçue d'un inconnu");
  await call("POST", "/friends/block", louis, { code: stranger.id });
  let lists = (await call("GET", "/friends", louis)).data;
  check(lists.incoming.length === 0 && lists.blocked.some(f => f.code === stranger.id && f.name === "Inconnu"), "inconnu bloqué : sa demande disparaît, il est dans « bloqués »");
  check((await call("POST", "/friends/add", stranger, { code: louis.id })).status === 404, "un bloqué ne peut plus redemander (« code inconnu »)");
  await call("POST", "/friends/block", louis, { code: max.id });
  check((await call("GET", "/friends", max)).data.friends.length === 0 && (await call("GET", "/friends", louis)).data.friends.length === 0,
    "Max bloqué : plus d'amitié, il ne voit plus la présence de Louis");
  check((await call("POST", "/messages/send", max, { code: louis.id, message: "play" })).status === 403, "un bloqué ne peut plus envoyer de message");
  await call("POST", "/friends/unblock", louis, { code: max.id });
  await call("POST", "/friends/add", louis, { code: max.id });
  await call("POST", "/friends/add", max, { code: louis.id });
  lists = (await call("GET", "/friends", louis)).data;
  check(lists.friends.length === 1 && !lists.blocked.some(f => f.code === max.id), "débloqué puis rajouté : amis de nouveau");
  await call("POST", "/friends/add", louis, { code: stranger.id });
  check(!(await call("GET", "/friends", louis)).data.blocked.some(f => f.code === stranger.id), "ajouter quelqu'un qu'on avait bloqué le débloque");
  await call("POST", "/forget", stranger);

  check((await call("POST", "/friends/add", louis, { code: louis.id })).status === 400 && (await call("POST", "/friends/add", louis, { code: "ZZZZ2222" })).status === 404,
    "s'ajouter soi-même ou un code inconnu : refusé");

  await call("POST", "/friends/remove", max, { code: louis.id });
  check((await call("GET", "/friends", louis)).data.friends.length === 0 && (await call("GET", "/friends", max)).data.incoming.length === 0,
    "ami retiré : plus rien des deux côtés");

  // Avis et bugs (7C)
  check((await call("GET", "/")).data.feedback === true, "le serveur annonce les avis (webhook configuré)");
  const sendFeedback = async (auth, fields, files = {}) => {
    const form = new FormData();
    for (const [key, value] of Object.entries(fields)) form.append(key, value);
    for (const [key, [bytes, type]] of Object.entries(files)) form.append(key, new Blob([bytes], { type }), key);
    const headers = auth ? { Authorization: `Bearer ${auth.id}.${auth.secret}` } : {};
    const response = await worker.fetch(new Request("https://amis.example/feedback", { method: "POST", headers, body: form }), env);
    return { status: response.status, data: await response.json() };
  };
  check((await sendFeedback(null, { kind: "bug", text: "x" })).status === 401, "avis sans compte : refusé");
  check((await sendFeedback(max, { kind: "bug", text: "   " })).status === 400, "avis vide : refusé");
  check((await sendFeedback(max, { kind: "bug", text: "x" }, { report: [new Uint8Array([1]), "text/html"] })).status === 400, "pièce jointe d'un type inattendu : refusée");
  const sent = await sendFeedback(max, { kind: "bug", text: "Le dock clignote @everyone", version: "1.24.0" },
    { screenshot: [new Uint8Array([0xff, 0xd8, 0xff]), "image/jpeg"], report: [new Uint8Array([0x50, 0x4b]), "application/zip"] });
  const payload = lastWebhook ? JSON.parse(lastWebhook.form.get("payload_json")) : null;
  check(sent.status === 200 && payload && payload.embeds[0].description === "Le dock clignote @everyone" && payload.allowed_mentions.parse.length === 0
    && payload.embeds[0].fields[0].value.startsWith("Max (") && lastWebhook.form.get("files[0]").name === "capture.jpg" && lastWebhook.form.get("files[1]").name === "rapport.zip",
    "avis transmis au salon : texte, auteur, capture et rapport, sans mention possible");
  for (let i = 0; i < 4; i++) await sendFeedback(max, { kind: "idea", text: "idée " + i });
  check((await sendFeedback(max, { kind: "idea", text: "une de trop" })).status === 429, "plus de 5 avis en une heure : refusé");
  const saved = env.FEEDBACK_WEBHOOK;
  delete env.FEEDBACK_WEBHOOK;
  check((await call("GET", "/")).data.feedback === false && (await sendFeedback(louis, { kind: "bug", text: "x" })).status === 503,
    "sans webhook : avis désactivés proprement");
  env.FEEDBACK_WEBHOOK = saved;

  await call("POST", "/forget", louis);
  check((await call("GET", "/friends", louis)).status === 401, "« Oublier » : le compte de Louis est effacé");

  console.log(failures === 0 ? "Tout est bon." : `${failures} échec(s).`);
  process.exitCode = failures ? 1 : 0;
}
