import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { createPartnerServer } from './server.mjs';
import { settingsFromEnvironment } from './server.mjs';

async function fixture(t) {
  const calls = [];
  let time = Date.now();
  const oauth = {
    async start() { return { state: 'fixture-state', nonce: 'fixture-nonce', verifier: 'private-verifier', challenge: 'public-challenge', authorizeUrl: 'https://issuer.example/oauth/authorize?state=fixture-state' }; },
    async complete(url, transaction) { calls.push({ url, transaction }); return { access_token: 'private-access-token', expires_in: 300, claims: () => ({ sub: 'pairwise-subject' }) }; },
    async profile() { return { sub: 'pairwise-subject', name: 'Demo' }; },
    async revoke() {},
  };
  const server = createPartnerServer({ origin: 'http://localhost', redirectUri: 'http://localhost/auth/ankt/callback', issuer: 'https://issuer.example', hostOrigin: 'https://host.example', clientId: 'client', localDevelopment: true }, oauth, () => time);
  server.listen(0, '127.0.0.1'); await once(server, 'listening');
  t.after(() => server.close());
  const origin = `http://127.0.0.1:${server.address().port}`;
  const request = (path, options = {}) => fetch(origin + path, { redirect: 'manual', ...options });
  const session = await request('/auth/ankt/session');
  const cookie = session.headers.get('set-cookie').split(';')[0];
  const { csrfToken } = await session.json();
  const post = path => request(path, { method: 'POST', headers: { Cookie: cookie, Origin: 'http://localhost', 'X-CSRF-Token': csrfToken } });
  return { request, cookie, csrfToken, post, calls, oauth, advance: milliseconds => { time += milliseconds; } };
}

test('start requires own origin and CSRF; verifier/secret never reaches browser', async t => {
  const f = await fixture(t);
  const missing = await f.request('/auth/ankt/start', { method: 'POST', headers: { Cookie: f.cookie, Origin: 'http://localhost' } });
  assert.equal(missing.status, 403);
  const attacker = await f.request('/auth/ankt/start', { method: 'POST', headers: { Cookie: f.cookie, Origin: 'https://attacker.example', 'X-CSRF-Token': f.csrfToken } });
  assert.equal(attacker.status, 403);
  const started = await f.post('/auth/ankt/start'); assert.equal(started.status, 200);
  const data = await started.json(); assert.equal(data.sdkParams.clientId, 'client');
  assert.equal(data.sdkParams.codeChallenge, 'public-challenge');
  assert.doesNotMatch(JSON.stringify(data), /private-verifier|client_secret|access_token/);
});

test('callback is bound to browser transaction, consumes once and rotates session', async t => {
  const f = await fixture(t); await f.post('/auth/ankt/start');
  const wrong = await f.request('/auth/ankt/callback?code=code&state=attacker-state', { headers: { Cookie: f.cookie } });
  assert.equal(wrong.status, 400); assert.equal(f.calls.length, 0);
  // Wrong state burns this transaction, so a fresh start is required.
  await f.post('/auth/ankt/start');
  const callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  assert.equal(callback.status, 303); assert.equal(callback.headers.get('location'), '/');
  const rotated = callback.headers.get('set-cookie').split(';')[0]; assert.notEqual(rotated, f.cookie);
  assert.equal(f.calls[0].transaction.verifier, 'private-verifier');
  assert.equal(f.calls[0].url.origin, 'http://localhost');
  const replay = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  assert.equal(replay.status, 400); assert.equal(f.calls.length, 1);
  const profile = await f.request('/auth/ankt/profile', { headers: { Cookie: rotated } });
  assert.equal(profile.status, 200); assert.doesNotMatch(await profile.text(), /private-access-token|access_token|id_token/);
  assert.match(callback.headers.get('set-cookie'), /HttpOnly/);
});

test('unauthenticated profile denied and logout requires CSRF', async t => {
  const f = await fixture(t);
  assert.equal((await f.request('/auth/ankt/profile', { headers: { Cookie: f.cookie } })).status, 401);
  assert.equal((await f.request('/auth/ankt/logout', { method: 'POST', headers: { Cookie: f.cookie } })).status, 403);
});

test('configuration refuses implicit issuer, public HTTP and callback query/fragment', () => {
  const env = { ANKT_OIDC_ISSUER: 'https://issuer.example', ANKT_HOST_ORIGIN: 'https://host.example', ANKT_REDIRECT_URI: 'https://partner.example/auth/ankt/callback', ANKT_CLIENT_ID: 'client', ANKT_CLIENT_SECRET: 'backend-secret' };
  assert.equal(settingsFromEnvironment(env).origin, 'https://partner.example');
  assert.throws(() => settingsFromEnvironment({ ...env, ANKT_OIDC_ISSUER: '' }));
  assert.throws(() => settingsFromEnvironment({ ...env, ANKT_OIDC_ISSUER: 'http://issuer.example', LOCAL_DEVELOPMENT: 'true' }));
  assert.throws(() => settingsFromEnvironment({ ...env, ANKT_REDIRECT_URI: env.ANKT_REDIRECT_URI + '?other=value' }));
  assert.throws(() => settingsFromEnvironment({ ...env, ANKT_REDIRECT_URI: env.ANKT_REDIRECT_URI + '#fragment' }));
});

test('expired browser transaction never exchanges its callback code', async t => {
  const f = await fixture(t); await f.post('/auth/ankt/start');
  f.advance(300_001);
  const callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  assert.equal(callback.status, 400); assert.equal(f.calls.length, 0);
});

test('embedded callback creates a real authenticated session only from a matching signed runtime claim', async t => {
  const f = await fixture(t);
  const start = () => f.request('/auth/ankt/start', { method: 'POST', headers: { Cookie: f.cookie, Origin: 'http://localhost', 'X-CSRF-Token': f.csrfToken }, body: JSON.stringify({ appId: 'app-a', runtimeSessionId: 'runtime-a' }) });
  await start();
  let callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  assert.equal(callback.status, 400); // Existing stub has no signed runtime claim.
  f.oauth.complete = async () => ({ access_token: 'private-access-token', expires_in: 300, claims: () => ({ sub: 'pairwise-subject', runtime_session_id: 'runtime-a' }) });
  await start();
  callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  assert.equal(callback.status, 303);
  const cookie = callback.headers.get('set-cookie').split(';')[0];
  const session = await f.request('/auth/ankt/session', { headers: { Cookie: cookie } });
  const body = await session.json();
  assert.equal(body.authenticated, true); assert.equal(body.runtimeSessionId, 'runtime-a');
  assert.equal((await f.request('/auth/ankt/profile', { headers: { Cookie: cookie } })).status, 200);
  assert.doesNotMatch(JSON.stringify(body), /private-access-token|access_token|id_token|verifier/);
});

test('revoked provider token invalidates otherwise live local session', async t => {
  const f = await fixture(t); await f.post('/auth/ankt/start');
  const callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  const cookie = callback.headers.get('set-cookie').split(';')[0];
  f.oauth.profile = async () => { throw new Error('provider_401'); };
  assert.equal((await f.request('/auth/ankt/profile', { headers: { Cookie: cookie } })).status, 401);
  const bootstrap = await f.request('/auth/ankt/session', { headers: { Cookie: cookie } });
  assert.equal((await bootstrap.json()).authenticated, false);
  assert.notEqual(bootstrap.headers.get('set-cookie').split(';')[0], cookie);
});

test('partner logout removes local identity even if provider revocation is temporarily unavailable', async t => {
  const f = await fixture(t); await f.post('/auth/ankt/start');
  const callback = await f.request('/auth/ankt/callback?code=code&state=fixture-state', { headers: { Cookie: f.cookie } });
  const cookie = callback.headers.get('set-cookie').split(';')[0];
  const session = await (await f.request('/auth/ankt/session', { headers: { Cookie: cookie } })).json();
  f.oauth.revoke = async () => { throw new Error('provider_unavailable'); };
  const response = await f.request('/auth/ankt/logout', { method: 'POST', headers: { Cookie: cookie, Origin: 'http://localhost', 'X-CSRF-Token': session.csrfToken } });
  assert.equal(response.status, 200);
  assert.deepEqual(await response.json(), { loggedOut: true, providerRevoked: false });
  assert.match(response.headers.get('set-cookie'), /Max-Age=0/);
  assert.equal((await f.request('/auth/ankt/profile', { headers: { Cookie: cookie } })).status, 401);
});
