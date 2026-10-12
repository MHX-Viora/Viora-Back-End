import test from 'node:test';
import assert from 'node:assert/strict';
import { once } from 'node:events';
import { generateKeyPairSync, sign, createHash } from 'node:crypto';
import { readFileSync } from 'node:fs';
import vm from 'node:vm';
import * as client from 'openid-client';
import { oidcAdapter } from './oidc.mjs';
import { createPartnerServer } from './server.mjs';

const issuer = 'https://issuer.example';
const partnerOrigin = 'https://partner.example';
const hostOrigin = 'https://host.example';
const redirectUri = `${partnerOrigin}/auth/ankt/callback`;
const sdkSource = readFileSync(new URL('../../viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js', import.meta.url), 'utf8');

test('production SDK and real BFF create a rotated session using signed synthetic OIDC, then isolate account switches', async t => {
  // Only the OIDC provider and ANKT host are synthetic. The SDK, HTTP BFF,
  // openid-client validation and partner cookie/session handling are production code.
  const keys = generateKeyPairSync('rsa', { modulusLength: 2048 });
  const jwk = { ...keys.publicKey.export({ format: 'jwk' }), kid: 'synthetic-key', alg: 'RS256', use: 'sig' };
  const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const jwt = claims => {
    const input = `${encode({ alg: 'RS256', kid: jwk.kid })}.${encode(claims)}`;
    return `${input}.${sign('RSA-SHA256', Buffer.from(input), keys.privateKey).toString('base64url')}`;
  };
  const config = new client.Configuration({ issuer, authorization_endpoint: `${issuer}/oauth/authorize`, token_endpoint: `${issuer}/oauth/token`, jwks_uri: `${issuer}/oauth/jwks`, userinfo_endpoint: `${issuer}/v1/userinfo` },
    'synthetic-client', { client_secret: 'synthetic-backend-secret', id_token_signed_response_alg: 'RS256' }, client.ClientSecretPost('synthetic-backend-secret'));
  client.enableNonRepudiationChecks(config);
  const grants = new Map(), subjects = new Map();
  let exchanges = 0;
  config[client.customFetch] = async (url, options) => {
    if (String(url) === `${issuer}/oauth/jwks`) return Response.json({ keys: [jwk] });
    if (String(url) === `${issuer}/v1/userinfo`) {
      const headers = new Headers(options.headers);
      const subject = subjects.get(headers.get('authorization')?.replace(/^Bearer /i, ''));
      assert.ok(subject, 'userinfo requires a provider-issued token held only by the BFF');
      return Response.json({ sub: subject, name: `Synthetic ${subject}` });
    }
    assert.equal(String(url), `${issuer}/oauth/token`);
    const form = new URLSearchParams(options.body);
    const grant = grants.get(form.get('code'));
    assert.ok(grant, 'authorization code must exist and be one-use');
    grants.delete(form.get('code'));
    assert.equal(form.get('client_secret'), 'synthetic-backend-secret');
    assert.equal(form.get('redirect_uri'), redirectUri);
    assert.equal(createHash('sha256').update(form.get('code_verifier')).digest('base64url'), grant.params.codeChallenge);
    const now = Math.floor(Date.now() / 1000);
    const token = `synthetic-provider-token-${++exchanges}`;
    subjects.set(token, grant.subject);
    return Response.json({ access_token: token, token_type: 'Bearer', expires_in: 300,
      id_token: jwt({ iss: issuer, sub: grant.subject, aud: 'synthetic-client', iat: now, exp: now + 300, nonce: grant.params.nonce, runtime_session_id: grant.runtimeSessionId }) });
  };

  const server = createPartnerServer({ origin: partnerOrigin, redirectUri, issuer, hostOrigin, clientId: 'synthetic-client', localDevelopment: true }, oidcAdapter(config, redirectUri));
  server.listen(0, '127.0.0.1');
  await once(server, 'listening');
  t.after(() => new Promise(resolve => server.close(resolve)));
  const loopback = `http://127.0.0.1:${server.address().port}`;
  let cookie = '';
  const cookies = [], requests = [], bridgeCalls = [];
  const request = async (path, options = {}) => {
    assert.match(path, /^\/auth\/ankt\//);
    requests.push({ path, method: options.method ?? 'GET', body: options.body });
    const headers = new Headers(options.headers);
    if (cookie) headers.set('Cookie', cookie);
    if (options.method === 'POST') headers.set('Origin', partnerOrigin);
    const response = await fetch(loopback + path, { ...options, headers, redirect: 'manual' });
    const setCookie = response.headers.get('set-cookie');
    if (setCookie) { cookies.push(setCookie); cookie = setCookie.split(';')[0]; }
    return response;
  };

  function page(runtimeSessionId, subject) {
    const listeners = {}, parent = {}, navigations = [];
    const binding = { type: 'ANKT_MINI_APP_READY', appId: 'synthetic-app', sessionId: runtimeSessionId, nonce: `binding-${runtimeSessionId}`, autoLogin: true };
    parent.postMessage = (message, origin) => {
      assert.equal(origin, hostOrigin);
      if (message.type === 'ANKT_MINI_APP_HELLO') return listeners.message({ data: binding, source: parent, origin });
      if (message.method === 'reportSsoStatus') return listeners.message({ source: parent, origin, data: { ...binding, type: 'ANKT_MINI_APP_RESPONSE', id: message.id, result: { received: true } } });
      assert.equal(message.method, 'requestOAuthLogin');
      assert.equal(message.sessionId, runtimeSessionId);
      assert.equal(message.appId, binding.appId);
      assert.equal(message.nonce, binding.nonce);
      bridgeCalls.push(message);
      const params = message.params;
      const code = `synthetic-code-${bridgeCalls.length}`;
      grants.set(code, { params, runtimeSessionId, subject });
      const authorization = new URL(`${issuer}/oauth/authorize`);
      for (const [key, value] of Object.entries({ client_id: params.clientId, redirect_uri: params.redirectUri, response_type: 'code', state: params.state, nonce: params.nonce, code_challenge: params.codeChallenge, code_challenge_method: 'S256', scope: params.scopes.join(' ') })) authorization.searchParams.set(key, value);
      listeners.message({ source: parent, origin, data: { ...binding, type: 'ANKT_MINI_APP_RESPONSE', id: message.id, result: { authorizeUrl: authorization.href } } });
    };
    const window = { parent, addEventListener: (name, handler) => { listeners[name] = handler; },
      location: { origin: partnerOrigin, assign: value => navigations.push(value) }, fetch: request };
    vm.runInNewContext(sdkSource, { window, URL, Map, Set, Date, Promise, Object, Error, JSON, setTimeout, clearTimeout });
    window.ANKT.configure({ hostOrigin, bff: true });
    return { sdk: window.ANKT, navigations };
  }

  async function complete(runtimeSessionId, subject) {
    const current = page(runtimeSessionId, subject);
    assert.equal((await current.sdk.auth.autoLogin()).status, 'redirecting');
    assert.equal(current.navigations.length, 1);
    assert.equal((await request('/auth/ankt/profile')).status, 401, 'a new runtime must stop serving any prior partner account before callback');
    const authorization = new URL(current.navigations[0]);
    assert.equal(authorization.origin, issuer);
    assert.equal(authorization.searchParams.get('redirect_uri'), redirectUri);
    assert.equal(authorization.searchParams.get('code_challenge_method'), 'S256');
    const beforeCallback = cookie;
    const callback = `/auth/ankt/callback?code=synthetic-code-${bridgeCalls.length}&state=${encodeURIComponent(authorization.searchParams.get('state'))}`;
    const response = await request(callback);
    assert.equal(response.status, 303);
    assert.equal(response.headers.get('location'), '/');
    assert.notEqual(cookie, beforeCallback, 'successful callback rotates the partner session');
    assert.match(response.headers.get('set-cookie'), /HttpOnly/);
    const replay = await request(callback);
    assert.equal(replay.status, 400, 'callback cannot replay after transaction consumption');
    const session = await (await request('/auth/ankt/session')).json();
    assert.equal(session.authenticated, true);
    assert.equal(session.runtimeSessionId, runtimeSessionId);
    const profile = await (await request('/auth/ankt/profile')).json();
    assert.equal(profile.sub, subject);
    assert.doesNotMatch(JSON.stringify({ session, profile }), /access_token|id_token|code_verifier|synthetic-backend-secret|synthetic-provider-token/);
    return profile;
  }

  const first = await complete('runtime-account-a', 'pairwise-account-a');
  assert.equal(exchanges, 1);
  const startsBeforeReuse = requests.filter(item => item.path === '/auth/ankt/start').length;
  const reused = page('runtime-account-a', 'pairwise-account-a');
  assert.equal((await reused.sdk.auth.autoLogin()).status, 'authenticated');
  assert.equal(reused.navigations.length, 0);
  assert.equal(requests.filter(item => item.path === '/auth/ankt/start').length, startsBeforeReuse);
  assert.equal(bridgeCalls.length, 1);

  const second = await complete('runtime-account-b', 'pairwise-account-b');
  assert.equal(exchanges, 2);
  assert.notEqual(second.accountId, first.accountId, 'changed ANKT subject must have a different partner account');
  assert.equal(bridgeCalls.length, 2);
  assert.ok(cookies.every(value => value.includes('HttpOnly')));
  for (const item of requests.filter(item => item.path === '/auth/ankt/start')) {
    const body = JSON.parse(item.body);
    assert.equal(body.appId, 'synthetic-app');
    assert.ok(['runtime-account-a', 'runtime-account-b'].includes(body.runtimeSessionId));
  }
  assert.doesNotMatch(JSON.stringify(bridgeCalls), /access_token|id_token|code_verifier|client_secret|synthetic-backend-secret|synthetic-provider-token/);
});
