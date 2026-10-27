import test from 'node:test';
import assert from 'node:assert/strict';
import { mkdtemp, rmdir, realpath, stat } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { join, dirname } from 'node:path';
import { closeOwnedBrowser } from './owned-browser-cleanup.mjs';

test('parent cleanup is idempotent after the child removed its profile and never acts on a reused PID', async () => {
  const profile = await mkdtemp(join(tmpdir(), 'ankt-sandbox-executor-'));
  assert.equal(dirname(await realpath(profile)).toLowerCase(), (await realpath(tmpdir())).toLowerCase());
  await rmdir(profile); // Child has already completed its browser/profile cleanup.
  const receipt = { pid: process.pid, profile, websocket: 'ws://127.0.0.1:65534/devtools/browser/already-closed' };
  await closeOwnedBrowser(receipt);
  await closeOwnedBrowser(receipt);
});

test('an existing profile with an unowned PID still fails closed and is preserved', { skip: process.platform !== 'win32' }, async () => {
  const profile = await mkdtemp(join(tmpdir(), 'ankt-sandbox-executor-'));
  assert.equal(dirname(await realpath(profile)).toLowerCase(), (await realpath(tmpdir())).toLowerCase());
  try {
    await assert.rejects(closeOwnedBrowser({ pid: process.pid, profile,
      websocket: 'ws://127.0.0.1:65534/devtools/browser/not-owned' }), /Browser ownership cleanup failed/);
    assert.equal((await stat(profile)).isDirectory(), true);
  } finally { await rmdir(profile); }
});
