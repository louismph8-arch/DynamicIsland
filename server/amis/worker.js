// Opaline — serveur « Amis en ligne » (6D-4), pour Cloudflare Workers + base D1 (offres gratuites).
// À coller tel quel dans un Worker Cloudflare, avec une base D1 liée sous le nom « DB » (voir LISEZMOI.md).
//
// Vie privée :
// - chaque PC reçoit un code d'ami (« ABCD-EFGH ») et un secret (gardé chiffré sur le PC, seule son empreinte est ici) ;
// - on ne voit que ses amis MUTUELS (chacun a ajouté le code de l'autre) : « en ligne » ou « joue à … », rien d'autre ;
// - la présence s'efface d'elle-même 10 minutes après les dernières nouvelles ; le mode invisible l'efface tout de suite ;
// - aucune adresse IP, aucun historique : seulement le pseudo, la présence du moment et la liste d'amis.
//
// Avis et bugs (7C, facultatif) : avec un secret « FEEDBACK_WEBHOOK » (adresse d'un webhook de salon Discord, Paramètres du
// Worker → Variables and Secrets), « Donner mon avis » dans Opaline arrive dans ce salon. Seuls les PC inscrits ici peuvent
// envoyer (5 par heure), l'adresse du webhook ne quitte jamais Cloudflare, et les mentions (@everyone…) sont désactivées.
//
// Version 3 (correctifs 23-24) :
// - BLOQUER : un bloqué ne voit plus ma présence (le lien est coupé dans les deux sens), ne peut plus me redemander en ami
//   (« code inconnu », comme si je n'existais pas) et ne peut plus m'envoyer de message ;
// - MESSAGES RAPIDES entre amis mutuels : un texte choisi dans une liste fermée (« On joue ? »…), jamais un texte libre ;
//   file courte par destinataire (10 au plus, 24 h au plus), effacée dès qu'elle est lue ; 20 envois par heure au plus ;
// - « depuis » : l'heure où l'ami s'est connecté ou a lancé son jeu (pour « Joue à Valorant depuis 14:32 »).
// Les anciennes versions d'Opaline continuent de marcher (elles ignorent ce qu'elles ne connaissent pas).

const VERSION = 3;
const FEEDBACK_PER_HOUR = 5;
const FEEDBACK_MAX_BYTES = 8 * 1024 * 1024; // pièces jointes (un message Discord en accepte 10 Mo)
const FEEDBACK_MAX_TEXT = 1800;
const ONLINE_FOR = 10 * 60 * 1000;      // présence valable 10 min sans nouvelles
const KEEP_PRESENCE = 60 * 60 * 1000;   // vieilles présences effacées après 1 h
const MAX_LINKS = 100;
const MAX_BLOCKS = 200;
const MESSAGES_PER_HOUR = 20;
const MAX_QUEUED = 10;                     // messages en attente par destinataire
const KEEP_MESSAGES = 24 * 60 * 60 * 1000; // un message jamais lu est effacé après 24 h
// Liste FERMÉE des messages rapides : seul l'identifiant voyage, chaque Opaline l'affiche dans sa langue.
const QUICK_MESSAGES = ["play", "available", "join", "later"];
const ALPHABET = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789"; // pas de 0/O ni de 1/I

const SCHEMA = [
  "CREATE TABLE IF NOT EXISTS users (id TEXT PRIMARY KEY, secret_hash TEXT NOT NULL, name TEXT NOT NULL DEFAULT '', created INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS presence (id TEXT PRIMARY KEY, status TEXT NOT NULL, game TEXT, updated INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS links (from_id TEXT NOT NULL, to_id TEXT NOT NULL, created INTEGER NOT NULL, PRIMARY KEY (from_id, to_id))",
  "CREATE INDEX IF NOT EXISTS links_to ON links (to_id)",
  "CREATE TABLE IF NOT EXISTS feedback (id TEXT NOT NULL, at INTEGER NOT NULL)",
  "CREATE TABLE IF NOT EXISTS blocks (from_id TEXT NOT NULL, to_id TEXT NOT NULL, created INTEGER NOT NULL, PRIMARY KEY (from_id, to_id))",
  "CREATE INDEX IF NOT EXISTS blocks_to ON blocks (to_id)",
  "CREATE TABLE IF NOT EXISTS messages (to_id TEXT NOT NULL, from_id TEXT NOT NULL, message TEXT NOT NULL, at INTEGER NOT NULL)",
  "CREATE INDEX IF NOT EXISTS messages_to ON messages (to_id)",
  "CREATE TABLE IF NOT EXISTS message_log (id TEXT NOT NULL, at INTEGER NOT NULL)",
];
// Colonnes ajoutées après coup à une base déjà créée par la version 2 : l'erreur « duplicate column » est ignorée.
const MIGRATIONS = [
  "ALTER TABLE presence ADD COLUMN since INTEGER",
];
let schemaReady = false;

export default {
  async fetch(request, env) {
    try {
      if (!schemaReady) {
        await env.DB.batch(SCHEMA.map(sql => env.DB.prepare(sql)));
        for (const sql of MIGRATIONS) {
          try {
            await env.DB.prepare(sql).run();
          } catch {
            // déjà fait
          }
        }
        schemaReady = true;
      }
      return await route(request, env);
    } catch (error) {
      return json({ error: "server", detail: String(error && error.message || error) }, 500);
    }
  },
};

async function route(request, env) {
  const db = env.DB;
  const url = new URL(request.url);
  const path = url.pathname.replace(/\/+$/, "") || "/";
  if (request.method === "GET" && path === "/") return json({ app: "opaline-amis", version: VERSION, feedback: Boolean(env.FEEDBACK_WEBHOOK), features: ["block", "messages"], messages: QUICK_MESSAGES });
  if (request.method === "POST" && path === "/register") return register(db, await body(request));

  const me = await authenticate(request, db);
  if (!me) return json({ error: "auth" }, 401);
  if (request.method === "POST" && path === "/me") return updateMe(db, me, await body(request));
  if (request.method === "GET" && path === "/friends") return friends(db, me);
  if (request.method === "POST" && path === "/friends/add") return addFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/friends/remove") return removeFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/friends/block") return blockFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/friends/unblock") return unblockFriend(db, me, await body(request));
  if (request.method === "POST" && path === "/messages/send") return sendMessage(db, me, await body(request));
  if (request.method === "GET" && path === "/messages") return readMessages(db, me);
  if (request.method === "POST" && path === "/forget") return forget(db, me);
  if (request.method === "POST" && path === "/feedback") return sendFeedback(request, env, me);
  return json({ error: "not_found" }, 404);
}

// ------------------------------------------------------------------ avis et bugs (7C)

/** Formulaire « Donner mon avis » d'Opaline → message dans le salon Discord du webhook (texte + capture + rapport facultatifs). */
async function sendFeedback(request, env, me) {
  if (!env.FEEDBACK_WEBHOOK) return json({ error: "feedback_off" }, 503);
  const db = env.DB;
  const now = Date.now();
  await db.prepare("DELETE FROM feedback WHERE at < ?").bind(now - 60 * 60 * 1000).run();
  const recent = await db.prepare("SELECT COUNT(*) AS n FROM feedback WHERE id = ?").bind(me).first();
  if (recent && recent.n >= FEEDBACK_PER_HOUR) return json({ error: "too_many" }, 429);

  let form;
  try {
    form = await request.formData();
  } catch {
    return json({ error: "bad_request" }, 400);
  }
  const kind = form.get("kind") === "bug" ? "bug" : "idea";
  const text = String(form.get("text") || "").replace(/[\u0000-\u0008\u000b-\u001f]/g, "").trim().slice(0, FEEDBACK_MAX_TEXT);
  if (!text) return json({ error: "empty" }, 400);
  const version = cleanName(String(form.get("version") || "")) || "?";

  const files = [];
  let size = 0;
  for (const [field, name, types] of [["screenshot", "capture.jpg", ["image/jpeg", "image/png"]], ["report", "rapport.zip", ["application/zip"]]]) {
    const file = form.get(field);
    if (!file || typeof file === "string" || file.size === 0) continue;
    if (!types.includes(file.type)) return json({ error: "bad_file" }, 400);
    size += file.size;
    files.push([file, name]);
  }
  if (size > FEEDBACK_MAX_BYTES) return json({ error: "too_big" }, 413);

  const user = await db.prepare("SELECT name FROM users WHERE id = ?").bind(me).first();
  const who = `${user && user.name ? user.name : "?"} (${me.slice(0, 4)}-${me.slice(4)})`;
  const payload = {
    username: "Opaline",
    allowed_mentions: { parse: [] }, // jamais de @everyone / @here / @quelqu'un depuis un avis
    embeds: [{
      title: kind === "bug" ? "🐞 Bug signalé" : "💬 Avis",
      description: text,
      color: kind === "bug" ? 0xff5e5e : 0x7c5cff,
      fields: [{ name: "De", value: who, inline: true }, { name: "Version", value: version, inline: true }],
      timestamp: new Date(now).toISOString(),
    }],
  };
  const out = new FormData();
  out.append("payload_json", JSON.stringify(payload));
  files.forEach(([file, name], index) => out.append(`files[${index}]`, file, name));
  const response = await fetch(env.FEEDBACK_WEBHOOK, { method: "POST", body: out });
  if (!response.ok) return json({ error: "discord", status: response.status }, 502);
  await db.prepare("INSERT INTO feedback (id, at) VALUES (?, ?)").bind(me, now).run();
  return json({ ok: true });
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
    db.prepare("DELETE FROM feedback WHERE id = ?").bind(me),
    db.prepare("DELETE FROM blocks WHERE from_id = ? OR to_id = ?").bind(me, me),
    db.prepare("DELETE FROM messages WHERE from_id = ? OR to_id = ?").bind(me, me),
    db.prepare("DELETE FROM message_log WHERE id = ?").bind(me),
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
    // « depuis » : gardé tant que l'ami reste dans le même état (même jeu) sans coupure de plus de 10 min.
    statements.push(db.prepare("INSERT INTO presence (id, status, game, updated, since) VALUES (?, ?, ?, ?, ?) " +
      "ON CONFLICT (id) DO UPDATE SET since = CASE WHEN presence.status = excluded.status AND IFNULL(presence.game, '') = IFNULL(excluded.game, '') " +
      "AND presence.updated > ? AND presence.since IS NOT NULL THEN presence.since ELSE excluded.since END, " +
      "status = excluded.status, game = excluded.game, updated = excluded.updated")
      .bind(me, input.status, game, now, now, now - ONLINE_FOR));
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
    "SELECT u.id, u.name, p.status, p.game, p.updated, p.since FROM links a " +
    "JOIN links b ON b.from_id = a.to_id AND b.to_id = a.from_id " +
    "JOIN users u ON u.id = a.to_id LEFT JOIN presence p ON p.id = u.id WHERE a.from_id = ? ORDER BY u.name")
    .bind(me).all();
  const incoming = await db.prepare(
    "SELECT u.id, u.name FROM links a JOIN users u ON u.id = a.from_id WHERE a.to_id = ? " +
    "AND NOT EXISTS (SELECT 1 FROM links b WHERE b.from_id = ? AND b.to_id = a.from_id) " +
    "AND NOT EXISTS (SELECT 1 FROM blocks k WHERE k.from_id = ? AND k.to_id = a.from_id)")
    .bind(me, me, me).all();
  const blocked = await db.prepare("SELECT k.to_id AS id, IFNULL(u.name, '') AS name FROM blocks k LEFT JOIN users u ON u.id = k.to_id WHERE k.from_id = ?")
    .bind(me).all();
  const outgoing = await db.prepare(
    "SELECT u.id, u.name FROM links a JOIN users u ON u.id = a.to_id WHERE a.from_id = ? " +
    "AND NOT EXISTS (SELECT 1 FROM links b WHERE b.from_id = a.to_id AND b.to_id = ?)")
    .bind(me, me).all();
  return json({
    friends: mutual.results.map(f => {
      const fresh = f.status && now - f.updated < ONLINE_FOR;
      return { code: f.id, name: f.name, status: fresh ? f.status : "offline", game: fresh ? f.game : null, since: fresh && f.since ? f.since : null };
    }),
    incoming: incoming.results.map(f => ({ code: f.id, name: f.name })),
    outgoing: outgoing.results.map(f => ({ code: f.id, name: f.name })),
    blocked: blocked.results.map(f => ({ code: f.id, name: f.name })),
  });
}

async function addFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code || code === me) return json({ error: "code" }, 400);
  const other = await db.prepare("SELECT id FROM users WHERE id = ?").bind(code).first();
  if (!other) return json({ error: "unknown" }, 404);
  // Bloqué par l'autre : comme si le code n'existait pas (rien ne dit qu'on a été bloqué).
  if (await isBlocked(db, code, me)) return json({ error: "unknown" }, 404);
  const count = await db.prepare("SELECT COUNT(*) AS n FROM links WHERE from_id = ?").bind(me).first();
  if (count.n >= MAX_LINKS) return json({ error: "full" }, 409);
  // Ajouter quelqu'un qu'on avait bloqué = le débloquer.
  await db.batch([
    db.prepare("DELETE FROM blocks WHERE from_id = ? AND to_id = ?").bind(me, code),
    db.prepare("INSERT OR IGNORE INTO links (from_id, to_id, created) VALUES (?, ?, ?)").bind(me, code, Date.now()),
  ]);
  return json({ ok: true });
}

/** Retirer un ami (ou refuser une demande) : le lien est coupé dans les deux sens. */
async function removeFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code) return json({ error: "code" }, 400);
  await db.prepare("DELETE FROM links WHERE (from_id = ? AND to_id = ?) OR (from_id = ? AND to_id = ?)").bind(me, code, code, me).run();
  return json({ ok: true });
}

// ------------------------------------------------------------------ blocage (version 3)

async function isBlocked(db, by, who) {
  return Boolean(await db.prepare("SELECT 1 AS x FROM blocks WHERE from_id = ? AND to_id = ?").bind(by, who).first());
}

/** Bloquer : liens coupés dans les deux sens, messages échangés effacés, et plus rien de cette personne ensuite. */
async function blockFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code || code === me) return json({ error: "code" }, 400);
  const count = await db.prepare("SELECT COUNT(*) AS n FROM blocks WHERE from_id = ?").bind(me).first();
  if (count.n >= MAX_BLOCKS) return json({ error: "full" }, 409);
  await db.batch([
    db.prepare("INSERT OR IGNORE INTO blocks (from_id, to_id, created) VALUES (?, ?, ?)").bind(me, code, Date.now()),
    db.prepare("DELETE FROM links WHERE (from_id = ? AND to_id = ?) OR (from_id = ? AND to_id = ?)").bind(me, code, code, me),
    db.prepare("DELETE FROM messages WHERE (from_id = ? AND to_id = ?) OR (from_id = ? AND to_id = ?)").bind(code, me, me, code),
  ]);
  return json({ ok: true });
}

async function unblockFriend(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code) return json({ error: "code" }, 400);
  await db.prepare("DELETE FROM blocks WHERE from_id = ? AND to_id = ?").bind(me, code).run();
  return json({ ok: true });
}

// ------------------------------------------------------------------ messages rapides (version 3)

/** Un message de la liste fermée, à un ami MUTUEL (ni bloqué ni bloquant) ; 20 envois par heure au plus. */
async function sendMessage(db, me, input) {
  const code = normalizeCode(input.code);
  if (!code || code === me) return json({ error: "code" }, 400);
  if (!QUICK_MESSAGES.includes(input.message)) return json({ error: "message" }, 400);
  const mutual = await db.prepare(
    "SELECT 1 AS x FROM links a JOIN links b ON b.from_id = a.to_id AND b.to_id = a.from_id WHERE a.from_id = ? AND a.to_id = ?")
    .bind(me, code).first();
  if (!mutual || await isBlocked(db, code, me) || await isBlocked(db, me, code)) return json({ error: "not_friend" }, 403);
  const now = Date.now();
  await db.prepare("DELETE FROM message_log WHERE at < ?").bind(now - 60 * 60 * 1000).run();
  const recent = await db.prepare("SELECT COUNT(*) AS n FROM message_log WHERE id = ?").bind(me).first();
  if (recent && recent.n >= MESSAGES_PER_HOUR) return json({ error: "too_many" }, 429);
  await db.batch([
    db.prepare("INSERT INTO messages (to_id, from_id, message, at) VALUES (?, ?, ?, ?)").bind(code, me, input.message, now),
    db.prepare("INSERT INTO message_log (id, at) VALUES (?, ?)").bind(me, now),
    // file courte : seulement les 10 plus récents, et rien de plus vieux que 24 h
    db.prepare("DELETE FROM messages WHERE to_id = ? AND rowid NOT IN (SELECT rowid FROM messages WHERE to_id = ? ORDER BY at DESC, rowid DESC LIMIT ?)")
      .bind(code, code, MAX_QUEUED),
    db.prepare("DELETE FROM messages WHERE at < ?").bind(now - KEEP_MESSAGES),
  ]);
  return json({ ok: true });
}

/** Mes messages en attente, effacés dès qu'ils sont lus. */
async function readMessages(db, me) {
  const rows = await db.prepare(
    "SELECT m.rowid AS rid, m.from_id, IFNULL(u.name, '') AS name, m.message, m.at FROM messages m LEFT JOIN users u ON u.id = m.from_id " +
    "WHERE m.to_id = ? ORDER BY m.at, m.rowid")
    .bind(me).all();
  if (rows.results.length) {
    const last = Math.max(...rows.results.map(r => r.rid));
    await db.prepare("DELETE FROM messages WHERE to_id = ? AND rowid <= ?").bind(me, last).run();
  }
  const fresh = rows.results.filter(r => Date.now() - r.at < KEEP_MESSAGES);
  return json({ messages: fresh.map(r => ({ code: r.from_id, name: r.name, message: r.message, at: r.at })) });
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
