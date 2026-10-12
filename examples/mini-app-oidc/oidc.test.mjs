import test from 'node:test';
import assert from 'node:assert/strict';
import { generateKeyPairSync, sign } from 'node:crypto';
import * as client from 'openid-client';
import { oidcAdapter } from './oidc.mjs';

const issuer = 'https://issuer.example';
const redirectUri = 'https://partner.example/auth/ankt/callback';
const keys = generateKeyPairSync('rsa', { modulusLength: 2048 });
const publicKey = { ...keys.publicKey.export({ format: 'jwk' }), kid: 'demo-key', alg: 'RS256', use: 'sig' };
function jwt(claims, signingKey = keys.privateKey) {
  const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
  const payload = `${encode({ alg: 'RS256', kid: 'demo-key' })}.${encode(claims)}`;
  return `${payload}.${sign('RSA-SHA256', Buffer.from(payload), signingKey).toString('base64url')}`;
}
async function fixture(overrides = {}, wrongSignature = false) {
  const config = new client.Configuration({ issuer, authorization_endpoint: `${issuer}/oauth/authorize`, token_endpoint: `${issuer}/oauth/token`, jwks_uri: `${issuer}/oauth/jwks` }, 'client', { client_secret: 'backend-secret', id_token_signed_response_alg: 'RS256' }, client.ClientSecretPost('backend-secret'));
  client.enableNonRepudiationChecks(config);
  const adapter = oidcAdapter(config, redirectUri);
  const transaction = await adapter.start();
  config[client.customFetch] = async (url, options) => {
    if (String(url).endsWith('/oauth/jwks')) return Response.json({ keys: [publicKey] });
    assert.equal(String(url), `${issuer}/oauth/token`);
    const form = new URLSearchParams(options.body);
    assert.equal(form.get('client_secret'), 'backend-secret');
    assert.equal(form.get('code_verifier'), transaction.verifier);
    assert.equal(form.get('redirect_uri'), redirectUri);
    const now = Math.floor(Date.now() / 1000);
    const claims = { iss: issuer, sub: 'pairwise', aud: 'client', iat: now, exp: now + 300, nonce: transaction.nonce, ...overrides };
    const signingKey = wrongSignature ? generateKeyPairSync('rsa', { modulusLength: 2048 }).privateKey : keys.privateKey;
    return Response.json({ access_token: 'backend-token', token_type: 'Bearer', expires_in: 300, id_token: jwt(claims, signingKey) });
  };
  return { adapter, transaction, callback: new URL(`${redirectUri}?code=one-use-code&state=${transaction.state}`) };
}
test('real openid-client validates standard callback and sends backend-only verifier/secret', async () => {
  const f = await fixture();
  const tokens = await f.adapter.complete(f.callback, f.transaction);
  assert.equal(tokens.claims().sub, 'pairwise');
  const authorize = new URL(f.transaction.authorizeUrl);
  assert.equal(authorize.searchParams.get('code_challenge_method'), 'S256');
  assert.equal(authorize.searchParams.get('nonce'), f.transaction.nonce);
  assert.equal(authorize.searchParams.get('client_secret'), null);
  assert.equal(authorize.searchParams.get('code_verifier'), null);
});
test('real openid-client rejects invalid state, nonce, issuer, audience, expiry and signature', async () => {
  const invalidState = await fixture(); invalidState.callback.searchParams.set('state', 'attacker');
  await assert.rejects(() => invalidState.adapter.complete(invalidState.callback, invalidState.transaction));
  for (const overrides of [{ nonce: 'attacker' }, { iss: 'https://attacker.example' }, { aud: 'another-client' }, { exp: 1 }]) {
    const f = await fixture(overrides);
    await assert.rejects(() => f.adapter.complete(f.callback, f.transaction));
  }
  const forged = await fixture({}, true);
  await assert.rejects(() => forged.adapter.complete(forged.callback, forged.transaction));
});
