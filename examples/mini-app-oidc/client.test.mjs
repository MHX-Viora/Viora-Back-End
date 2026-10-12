import test from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';

test('automatic session reuse followed by logout enables manual retry and clears displayed identity', async () => {
  const elements = Object.fromEntries(['status', 'login', 'logout', 'profile'].map(id => [id, { hidden: true, disabled: false, textContent: '', handlers: {}, addEventListener(name, handler) { this.handlers[name] = handler; } }]));
  const sdk = { configure() {}, ready: async () => {}, auth: { autoLogin: async () => ({ status: 'authenticated' }), logout: async () => ({ loggedOut: true, providerRevoked: false }) }, user: { getProfile: async () => ({ sub: 'pairwise-test-only' }) } };
  const window = { parent: {} };
  const context = { window, ANKT: sdk, document: { querySelector: id => elements[id.slice(1)] }, fetch: async () => ({ ok: true, json: async () => ({ authenticated: true, issuer: 'https://issuer.example', hostOrigin: 'https://host.example' }) }) };
  const source = readFileSync(new URL('./client.mjs', import.meta.url), 'utf8');
  await vm.runInNewContext(`(async function(){${source}\n})()`, context);
  assert.match(elements.profile.textContent, /pairwise-test-only/);
  await elements.logout.handlers.click();
  assert.equal(elements.profile.textContent, '');
  assert.equal(elements.login.hidden, false); assert.equal(elements.login.disabled, false);
  assert.equal(elements.logout.hidden, true);
  assert.match(elements.status.textContent, /chưa thu hồi được/);
});
