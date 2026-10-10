import { test, after } from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';

process.env.NODE_ENV = 'development';
process.env.PORT = '5391';
process.env.MINI_APP_ORIGIN = 'http://127.0.0.1:5391';
process.env.MINI_APP_DATABASE = ':memory:';
process.env.ANKT_CLIENT_ID = 'ank_test_example';
const {server, db} = await import('./server.mjs');
if (!server.listening) await once(server, 'listening');
after(async () => { await new Promise(resolve => server.close(resolve)); db.close(); });
const origin = process.env.MINI_APP_ORIGIN;
async function browser() {
  const response = await fetch(origin); const html = await response.text();
  return { cookie: response.headers.get('set-cookie').split(';')[0], csrf: html.match(/name="csrf" value="([^"]+)"/)[1] };
}
async function post(path, current, fields, requestOrigin = origin) {
  return fetch(origin + path, {method: 'POST', headers: {'content-type': 'application/x-www-form-urlencoded', cookie: current.cookie, origin: requestOrigin}, body: new URLSearchParams({csrf: current.csrf, ...fields}), redirect: 'manual'});
}
test('partner account registration rotates session and retains independent login', async () => {
  const initial = await browser(); const registered = await post('/register', initial, {username:'existing',password:'example-password-123'});
  assert.equal(registered.status, 303); const newCookie = registered.headers.get('set-cookie').split(';')[0]; assert.notEqual(newCookie, initial.cookie);
  const dashboard = await fetch(origin, {headers: {cookie: newCookie}}); assert.match(await dashboard.text(), /existing/);
  const another = await browser(); const loggedIn = await post('/login', another, {username:'existing',password:'example-password-123'}); assert.equal(loggedIn.status, 303);
});
test('mutations reject cross-origin requests and missing CSRF', async () => {
  const current = await browser(); assert.equal((await post('/register', current, {username:'bad-origin',password:'example-password-123'}, 'https://untrusted.invalid')).status, 403);
  assert.equal((await post('/register', {...current, csrf:'invalid'}, {username:'bad-csrf',password:'example-password-123'})).status, 403);
});
test('linking requires password reauthentication and never trusts callback parameters alone', async () => {
  const current = await browser(); assert.equal((await post('/auth/ankt/start', current, {intent:'link',password:'example-password-123'})).status, 401);
  assert.equal((await fetch(origin + '/auth/ankt/callback?code=example&state=example', {headers:{cookie:current.cookie}})).status, 400);
  assert.equal(db.prepare('SELECT COUNT(*) AS count FROM identities').get().count, 0);
});
test('SSO transaction is bound to the initiating session and uses S256 challenge', async () => {
  const current = await browser(); const started = await post('/auth/ankt/start', current, {intent:'login'}); assert.equal(started.status, 200);
  const html = await started.text(); assert.match(html, /"codeChallengeMethod":"S256"/);
  const state = html.match(/"state":"([A-Za-z0-9_-]+)"/)[1]; const other = await browser();
  assert.equal((await fetch(origin + `/auth/ankt/callback?code=example&state=${state}`, {headers: {cookie:other.cookie}})).status, 400);
  assert.equal(db.prepare('SELECT COUNT(*) AS count FROM identities').get().count, 0);
});
