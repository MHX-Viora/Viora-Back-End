import { test } from 'node:test';
import assert from 'node:assert/strict';
import { DatabaseSync } from 'node:sqlite';
import { initializeIdentitySchema, resolveIdentity, unlinkIdentity, IdentityConflict } from './identity-store.mjs';

function fixture() {
  const db = new DatabaseSync(':memory:'); initializeIdentitySchema(db);
  db.prepare('INSERT INTO users VALUES (?, ?, ?, ?)').run('old-user', 'old', 'Existing user', 'verified-password-hash');
  db.prepare('INSERT INTO users VALUES (?, ?, ?, ?)').run('other-user', 'other', 'Other user', 'verified-password-hash');
  return db;
}

test('login creates an independent local account and keeps its identity stable', () => {
  const db = fixture();
  try { const id = resolveIdentity(db, 'sub_new', 'New user', 'login', null); assert.notEqual(id, 'old-user'); assert.equal(resolveIdentity(db, 'sub_new', 'Changed name', 'login', null), id); }
  finally { db.close(); }
});
test('link requires an existing authenticated account', () => {
  const db = fixture(); try { assert.throws(() => resolveIdentity(db, 'sub_new', '', 'link', null), IdentityConflict); assert.throws(() => resolveIdentity(db, 'sub_new', '', 'link', 'nonexistent'), IdentityConflict); } finally { db.close(); }
});
test('explicit link preserves the existing account and is idempotent', () => {
  const db = fixture(); try { assert.equal(resolveIdentity(db, 'sub_old', '', 'link', 'old-user'), 'old-user'); assert.equal(resolveIdentity(db, 'sub_old', '', 'link', 'old-user'), 'old-user'); assert.equal(resolveIdentity(db, 'sub_old', '', 'login', null), 'old-user'); } finally { db.close(); }
});
test('a subject cannot be linked to two local accounts', () => {
  const db = fixture(); try { resolveIdentity(db, 'sub_old', '', 'link', 'old-user'); assert.throws(() => resolveIdentity(db, 'sub_old', '', 'link', 'other-user'), IdentityConflict); assert.equal(db.prepare('SELECT COUNT(*) AS count FROM identities').get().count, 1); } finally { db.close(); }
});
test('an account cannot silently switch its linked subject', () => {
  const db = fixture(); try { resolveIdentity(db, 'sub_old', '', 'link', 'old-user'); assert.throws(() => resolveIdentity(db, 'sub_other', '', 'link', 'old-user'), IdentityConflict); } finally { db.close(); }
});
test('unlink preserves the local account and removes identity login', () => {
  const db = fixture(); try { resolveIdentity(db, 'sub_old', '', 'link', 'old-user'); unlinkIdentity(db, 'old-user'); assert.equal(db.prepare('SELECT COUNT(*) AS count FROM identities').get().count, 0); assert.ok(db.prepare('SELECT id FROM users WHERE id = ?').get('old-user')); assert.notEqual(resolveIdentity(db, 'sub_old', '', 'login', null), 'old-user'); } finally { db.close(); }
});
test('unlink cannot remove the last login method', () => {
  const db = fixture(); try { const id = resolveIdentity(db, 'sub_new', '', 'login', null); assert.throws(() => unlinkIdentity(db, id), IdentityConflict); assert.equal(resolveIdentity(db, 'sub_new', '', 'login', null), id); } finally { db.close(); }
});
