import { randomUUID } from 'node:crypto';

export class IdentityConflict extends Error { constructor(message) { super(message); this.status = 409; } }

// One sample deployment represents one ANKT client. No email-based account matching.
export function resolveIdentity(db, subject, displayName, intent, localUserId) {
  if (typeof subject !== 'string' || !subject.startsWith('sub_') || subject.length > 64) throw new Error('Invalid ANKT subject');
  db.exec('BEGIN IMMEDIATE');
  try {
    const linked = db.prepare('SELECT user_id FROM identities WHERE provider = ? AND subject = ?').get('ANKT', subject);
    let userId;
    if (intent === 'link') {
      if (!localUserId || !db.prepare('SELECT id FROM users WHERE id = ?').get(localUserId)) throw new IdentityConflict('Sign in to the existing account first');
      if (linked && linked.user_id !== localUserId) throw new IdentityConflict('This ANKT identity belongs to another account');
      const own = db.prepare('SELECT subject FROM identities WHERE provider = ? AND user_id = ?').get('ANKT', localUserId);
      if (own && own.subject !== subject) throw new IdentityConflict('This account is linked to a different ANKT identity');
      userId = localUserId;
    } else if (intent === 'login') {
      userId = linked?.user_id;
      if (!userId) {
        userId = randomUUID();
        db.prepare('INSERT INTO users(id, username, display_name, password_hash) VALUES (?, ?, ?, NULL)').run(userId, `ankt_${randomUUID()}`, String(displayName || 'ANKT user').slice(0, 120));
      }
    } else throw new Error('Invalid identity intent');
    if (!linked) db.prepare('INSERT INTO identities(provider, subject, user_id) VALUES (?, ?, ?)').run('ANKT', subject, userId);
    db.exec('COMMIT');
    return userId;
  } catch (error) { db.exec('ROLLBACK'); throw error; }
}

export function unlinkIdentity(db, userId) {
  const user = db.prepare('SELECT password_hash FROM users WHERE id = ?').get(userId);
  if (!user?.password_hash) throw new IdentityConflict('Set up and verify another login method before unlinking');
  db.prepare('DELETE FROM identities WHERE provider = ? AND user_id = ?').run('ANKT', userId);
  db.prepare('DELETE FROM transactions WHERE user_id = ?').run(userId);
}

export function initializeIdentitySchema(db) {
  db.exec(`PRAGMA foreign_keys = ON;
    CREATE TABLE IF NOT EXISTS users(id TEXT PRIMARY KEY, username TEXT UNIQUE NOT NULL, display_name TEXT NOT NULL, password_hash TEXT);
    CREATE TABLE IF NOT EXISTS identities(provider TEXT NOT NULL, subject TEXT NOT NULL, user_id TEXT NOT NULL REFERENCES users(id), PRIMARY KEY(provider, subject), UNIQUE(provider, user_id));
    CREATE TABLE IF NOT EXISTS sessions(id_hash TEXT PRIMARY KEY, user_id TEXT REFERENCES users(id), csrf TEXT NOT NULL, expires_at INTEGER NOT NULL);
    CREATE TABLE IF NOT EXISTS transactions(state_hash TEXT PRIMARY KEY, session_hash TEXT NOT NULL REFERENCES sessions(id_hash), verifier TEXT NOT NULL, intent TEXT NOT NULL, user_id TEXT REFERENCES users(id), expires_at INTEGER NOT NULL);`);
}
