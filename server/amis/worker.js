// Opaline — serveur « Amis en ligne » (6D-4), pour Cloudflare Workers + base D1 (offres gratuites).
// À coller tel quel dans un Worker Cloudflare, avec une base D1 liée sous le nom « DB » (voir LISEZMOI.md).
//
// Vie privée :
// - chaque PC reçoit un code d'ami (« ABCD-EFGH ») et un secret (gardé chiffré sur le PC, seule son empreinte est ici) ;
// - on ne voit que ses amis MUTUELS (chacun a ajouté le code de l'autre) : « en ligne » ou « joue à … », rien d'autre ;
// - la présence s'efface d'elle-même 10 minutes après les dernières nouvelles ; le mode invisible l'efface tout de suite ;
// - aucune adresse IP, aucun historique : seulement le pseudo, la présence du moment et la liste d'amis.

const VERSION = 1;
const ONLINE_FOR = 10 * 60 * 1000;      // présence valable 10 min sans nouvelles
const KEEP_PRESENCE = 60 * 60 * 1000;   // vieilles présences effacées après 1 h
const MAX_LINKS = 100;
const ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // pas de 0/O ni de 1/I

const SCHEMA = [
  "CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, secret_hash TEXT NOT NULL, name TEXT NOT NULL DEFAULT '', created INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS presence (id TEXT PRIMARY KEY, status TEXT NOT NULL, game TEXT, updated INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS links (from_id TEXT NOT NULL, to_id TEXT NOT NULL, created INTEGER NOT NULL, PRIMARY KEY (from_id, to_id))",
  "CREATE INDEX IF NOT EXISTS links_to ON links (to_id)",
];
let schemaReady = false;

export default {
  async fetch(request, env) {
    try {
      if (!schemaReady) {
        await env.DB.batch(SCHEMA.map(sql => env.DB.prepare(sql)));
        schemaReady = true;
      }
      return await route(request, env.DB);
    } catch (error) {
      return json({ error: "server", detail: String(error && error.message || error) }, 500);
    }
  },
};

async function route(request, db) {
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, "") || "/";
  if (request.method === "GET" && path === "/") return json({ app: "opaline-amis", version: VERSION });
  if (request.method === "POST" && path === "/register") return register(db, await body(request));

  const me = await authenticate(request, db);
  if (!me) return json({ error: "auth" }, 401);
  if (request.method === "POST" && path === "/me") return updateMe(db, me, await body(request));
  if (request.method === "GET" && path === "/friends") return friends(db, me);
  if (request.method === "POST" && path === "/friends/add") return addFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/friends/remove") return removeFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/forget") return forget(db, me);
  return json({ error: "not_found" }, 404);
}

// ------------------------------------------------------------------ comptes

async function register(db, input) {
  const secret = randomToken(32);
  for (let attempt = 0; attempt < 5; attempt++) {
    const id = randomCode(8);
    const result = await db.prepare("INSERT OR IGNORE INTO users (id, secret_hash, name, created) VALUES (?, ?, ?, ?)")
      .bind(id, await sha256(secret), cleanName(input.name), Date.now()).run();
    if (result.meta && result.meta.changes === 1) return json({ id, secret });
  }
  return json({ error: "busy" }, 503);
}

async function authenticate(request, db) {
  const header = request.headers.get("Authorization") || "";
  const match = /^Bearer ([A-Z0-9]{8})\.([A-Za-z0-9_-]{20,})$/.exec(header);
  if (!match) return null;
  const user = await db.prepare("SELECT id, secret_hash FROM users WHERE id = ?").bind(match[1]).first();
  if (!user) return null;
  return equal(user.secret_hash, await sha256(match[2])) ? user.id : null;
}

/** Tout effacer (« Quitter les amis en ligne » dans Opaline). */
async function forget(db, me) {
  await db.batch([
    db.prepare("DELETE FROM presence WHERE id = ?").bind(me),
    db.prepare("DELETE FROM links WHERE from_id = ? OR to_id = ?").bind(me, me),
    db.prepare("DELETE FROM users WHERE id = ?").bind(me),
  ]);
  return json({ ok: true });
}

// ------------------------------------------------------------------ présence

/** Battement de cœur : pseudo, « online » / « game » (+ nom du jeu), ou invisible / hors ligne = présence effacée. */
async function updateMe(db, me, input) {
  const now = Date.now();
  const statements = [];
  if (typeof input.name === "string") statements.push(db.prepare("UPDATE users SET name = ? WHERE id = ?").bind(cleanName(input.name), me));
  const visible = !input.invisible && (input.status === "online" || input.status === "game");
  if (visible) {
    const game = input.status === "game" && typeof input.game === "string" ? input.game.trim().slice(0, 60) || null : null;
    statements.push(db.prepare("INSERT INTO presence (id, status, game, updated) VALUES (?, ?, ?, ?) " +
      "ON CONFLICT (id) DO UPDATE SET status = excluded.status, game = excluded.game, updated = excluded.updated")
      .bind(me, input.status, game, now));
  } else {
    statements.push(db.prepare("DELETE FROM presence WHERE id = ?").bind(me));
  }
  if (Math.random() < 0.02) statements.push(db.prepare("DELETE FROM presence WHERE updated < ?").bind(now - KEEP_PRESENCE));
  await db.batch(statements);
  return json({ ok: true });
}

// ------------------------------------------------------------------ amis

async function friends(db, me) {
  const now = Date.now();
  const mutual = await db.prepare(
    "SELECT u.id, u.name, p.status, p.game, p.updated FROM links a " +
    "JOIN links b ON b.from_id = a.to_id AND b.to_id = a.from_id " +
    "JOIN users u ON u.id = a.to_id LEFT JOIN presence p ON p.id = u.id WHERE a.from_id = ? ORDER BY u.name")
    .bind(me).all();
  const incoming = await db.prepare(
    "SELECT u.id, u.name FROM links a JOIN users u ON u.id = a.from_id WHERE a.to_id = ? " +
    "AND NOT EXISTS (SELECT 1 FROM links b WHERE b.from_id = ? AND b.to_id = a.from_id)")
    .bind(me, me).all();
  const outgoing = await db.prepare(
    "SELECT u.id, u.name FROM links a JOIN users u ON u.id = a.to_id WHERE a.from_id = ? " +
    "AND NOT EXISTS (SELECT 1 FROM links b WHERE b.from_id = a.to_id AND b.to_id = ?)")
    .bind(me, me).all();
  return json({
    friends: mutual.results.map(f => {
      const fresh = f.status && now - f.updated < ONLINE_FOR;
      return { code: f.id, name: f.name, status: fresh ? f.status : "offline", game: fresh ? f.game : null };
    }),
    incoming: incoming.results.map(f => ({ code: f.id, name: f.name })),
    outgoing: outgoing.results.map(f => ({ code: f.id, name: f.name })),
  });
}

async function addFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code || code === me) return json({ error: "code" }, 400);
  const other = await db.prepare("SELECT id FROM users WHERE id = ?").bind(code).first();
  if (!other) return json({ error: "unknown" }, 404);
  const count = await db.prepare("SELECT COUNT(*) AS n FROM links WHERE from_id = ?").bind(me).first();
  if (count.n >= MAX_LINKS) return json({ error: "full" }, 409);
  await db.prepare("INSERT OR IGNORE INTO links (from_id, to_id, created) VALUES (?, ?, ?)").bind(me, code, Date.now()).run();
  return json({ ok: true });
}

/** Retirer un ami (ou refuser une demande) : le lien est coupé dans les deux sens. */
async function removeFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code) return json({ error: "code" }, 400);
  await db.prepare("DELETE FROM links WHERE (from_id = ? AND to_id = ?) OR (from_id = ? AND to_id = ?)").bind(me, code, code, me).run();
  return json({ ok: true });
}

// ------------------------------------------------------------------ outils

function json(value, status = 200) {
  return new Response(JSON.stringify(value), { status, headers: { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store" } });
}

async function body(request) {
  try {
    const text = await request.text();
    return text.length > 4096 ? {} : JSON.parse(text || "{}") || {};
  } catch {
    return {};
  }
}

function cleanName(name) {
  return typeof name === "string" ? name.replace(/[\u0000-\u001f]/g, "").trim().slice(0, 32) : "";
}

function normalizeCode(code) {
  if (typeof code !== "string") return null;
  const clean = code.toUpperCase().replace(/[\s-]/g, "");
  return /^[A-Z0-9]{8}$/.test(clean) ? clean : null;
}

function randomCode(length) {
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  return Array.from(bytes, b => ALPHABET[b % ALPHABET.length]).join("");
}

function randomToken(length) {
  const bytes = crypto.getRandomValues(new Uint8Array(length));
  return btoa(String.fromCharCode(...bytes)).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/, "");
}

async function sha256(text) {
  const digest = await crypto.subtle.digest("SHA-256", new TextEncoder().encode(text));
  return Array.from(new Uint8Array(digest), b => b.toString(16).padStart(2, "0")).join("");
}

function equal(a, b) {
  if (a.length !== b.length) return false;
  let diff = 0;
  for (let i = 0; i < a.length; i++) diff |= a.charCodeAt(i) ^ b.charCodeAt(i);
  return diff === 0;
}
