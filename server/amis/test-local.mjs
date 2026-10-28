// Essai du serveur « Amis en ligne » SANS Cloudflare (Node.js 22.5+, SQLite intégré, rien à télécharger) :
//   node test-local.mjs            → scénario complet (deux PC, demande d'ami, présence, invisible, retrait, oubli)
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

  check((await call("POST", "/friends/add", louis, { code: louis.id })).status === 400 && (await call("POST", "/friends/add", louis, { code: "ZZZZ2222" })).status === 404,
    "s'ajouter soi-même ou un code inconnu : refusé");

  await call("POST", "/friends/remove", max, { code: louis.id });
  check((await call("GET", "/friends", louis)).data.friends.length === 0 && (await call("GET", "/friends", max)).data.incoming.length === 0,
    "ami retiré : plus rien des deux côtés");

  await call("POST", "/forget", louis);
  check((await call("GET", "/friends", louis)).status === 401, "« Oublier » : le compte de Louis est effacé");

  console.log(failures === 0 ? "Tout est bon." : `${failures} échec(s).`);
  process.exitCode = failures ? 1 : 0;
}
