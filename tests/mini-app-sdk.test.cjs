const { test } = require('node:test');
const assert = require('node:assert/strict');
const vm = require('node:vm');
const fs = require('node:fs');
const sdk = fs.readFileSync('viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js', 'utf8');

function fixture(native = false) {
  const listeners = {}, sent = [], parent = {}, timers = new Map(); let timerId = 0;
  const window = {
    parent, addEventListener(type, callback) { listeners[type] = callback; },
    ReactNativeWebView: native ? { postMessage(raw) { sent.push(JSON.parse(raw)); } } : undefined,
  };
  parent.postMessage = (message, origin) => sent.push({ ...message, targetOrigin: origin });
  vm.runInNewContext(sdk, { window, URL, Set, Map, Date, Promise, Error, Object, JSON,
    setTimeout(callback) { const id = ++timerId; timers.set(id, callback); return id; }, clearTimeout(id) { timers.delete(id); } });
  const binding = { type: 'ANKT_MINI_APP_READY', appId: 'app_1', sessionId: 'session_1', nonce: 'nonce_abcdefghijklmnop' };
  function message(data, source = parent, origin = 'https://ankt.example.com') { listeners.message({ data, source, origin }); }
  function ready() { native ? window.__ANKT_NATIVE_READY__(binding) : message(binding); }
  function reply(result, overrides = {}) { const request = sent.at(-1); message({ ...binding, type: 'ANKT_MINI_APP_RESPONSE', id: request.id, result, ...overrides }, native ? null : parent); }
  return { window, listeners, sent, parent, timers, binding, message, ready, reply };
}

test('web handshake rejects unconfigured, wrong-origin and wrong-source responders', async () => {
  const f = fixture(); f.message(f.binding); f.window.ANKT.configure({ hostOrigin: 'https://ankt.example.com' });
  f.message(f.binding, {}, 'https://ankt.example.com'); f.message(f.binding, f.parent, 'https://evil.example.com');
  const pending = f.window.ANKT.getPlatformInfo(); await Promise.resolve(); assert.equal(f.sent.length, 0);
  f.ready(); await Promise.resolve(); assert.equal(f.sent.length, 1); assert.equal(f.sent[0].targetOrigin, 'https://ankt.example.com');
  f.reply({ platform: 'web' }); assert.equal((await pending).platform, 'web');
});
test('native handshake requires injected callback and ignores frame messages', async () => {
  const f = fixture(true); f.message(f.binding, null); const pending = f.window.ANKT.getAppInfo(); await Promise.resolve(); assert.equal(f.sent.length, 0);
  f.ready(); await Promise.resolve(); assert.equal(f.sent.length, 1);
  f.reply({ id: 'app_1' }); assert.equal((await pending).id, 'app_1');
});
test('responses require matching source, nonce and request ID', async () => {
  const f = fixture(); f.window.ANKT.configure({ hostOrigin: 'https://ankt.example.com' }); f.ready();
  const pending = f.window.ANKT.getGrantedPermissions(); await Promise.resolve();
  f.reply(['profile.email'], { nonce: 'wrong_nonce' }); assert.equal(f.timers.size, 1);
  f.message({ ...f.binding, type: 'ANKT_MINI_APP_RESPONSE', id: f.sent[0].id, result: ['profile.email'] }, {}); assert.equal(f.timers.size, 1);
  f.reply(['identity.login']); assert.deepEqual(Array.from(await pending), ['identity.login']);
});
test('invalid state, PKCE and permission input never reaches host', async () => {
  const f = fixture(true); f.ready();
  await assert.rejects(f.window.ANKT.requestLogin({ redirectUri: 'https://partner.example.com/callback', state: 'short', codeChallenge: 'a'.repeat(43), codeChallengeMethod: 'S256' }), { code: 'INVALID_REQUEST' });
  await assert.rejects(f.window.ANKT.requestPermission('invalid permission'), { code: 'INVALID_REQUEST' }); assert.equal(f.sent.length, 0);
});
test('login carries transaction binding and no host credentials', async () => {
  const f = fixture(true); f.ready(); const pending = f.window.ANKT.requestLogin({ redirectUri: 'https://partner.example.com/callback', state: 'state_abcdefghijklmnop', codeChallenge: 'a'.repeat(43), codeChallengeMethod: 'S256' });
  await Promise.resolve(); const request = f.sent[0]; assert.equal(request.method, 'requestLogin'); assert.equal(request.appId, 'app_1');
  assert.equal(request.sessionToken, undefined); assert.equal(request.accessToken, undefined); f.reply({ launchUrl: 'https://partner.example.com/callback?code=abc', expiresIn: 60 }); assert.equal((await pending).expiresIn, 60);
});
test('navigation rejects pending requests and handshake waiters', async () => {
  const f = fixture(true); f.ready(); const pending = f.window.ANKT.getAppInfo(); await Promise.resolve(); f.listeners.pagehide(); await assert.rejects(pending, { code: 'SESSION_CLOSED' });
  const waiter = f.window.ANKT.ready(); f.listeners.pagehide(); await assert.rejects(waiter, { code: 'SESSION_CLOSED' });
});
test('SDK limits request rate before invoking host', async () => {
  const f = fixture(true); f.ready();
  for (let i = 0; i < 30; i++) { const pending = f.window.ANKT.getPlatformInfo(); await Promise.resolve(); f.reply({}); await pending; }
  await assert.rejects(f.window.ANKT.getPlatformInfo(), { code: 'RATE_LIMITED' }); assert.equal(f.sent.length, 30);
});
