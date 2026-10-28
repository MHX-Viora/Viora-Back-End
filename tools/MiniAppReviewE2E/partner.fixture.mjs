import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';
import { createEmbeddedPartner } from '../MiniAppProductionE2E/production-partner.fixture.mjs';
const require = createRequire(resolve(import.meta.dirname, '../../../viora/packages/miniapp-sdk/package.json'));
const oidc = await import(pathToFileURL(require.resolve('openid-client')).href);
// Real Production PKCE/OIDC partner. Secrets, codes and tokens stay in this closure.
export function productionPartner(config) {
  let clientId, secret, session, currentClient, held, hold = false;
  const transactions = new Map(), redirectUri = 'https://partner.ankt.example.com/production/callback';
  async function client() {
    if (!clientId || config.partnerClientType === 'Confidential' && !secret) throw Error('Production credential not prepared');
    return currentClient ??= await oidc.discovery(new URL(config.issuer), clientId, { token_endpoint_auth_method: secret ? 'client_secret_basic' : 'none', id_token_signed_response_alg: 'RS256' }, secret ? oidc.ClientSecretBasic(secret) : oidc.None(), {
      execute: [oidc.enableNonRepudiationChecks], [oidc.customFetch]: (url, options) => { if (new URL(url).origin !== new URL(config.issuer).origin) throw Error('Unexpected issuer'); return fixtureFetch(url, { ...options, redirect: 'error' }); },
    });
  }
  async function exchange(url, transaction) {
    const runtime = await client();
    const tokens = await oidc.authorizationCodeGrant(runtime, url, { pkceCodeVerifier: transaction.verifier, expectedState: transaction.state, expectedNonce: transaction.nonce, idTokenExpected: true });
    const claims = tokens.claims(); if (!claims?.sub || !tokens.access_token) throw Error('OIDC validation failed');
    await oidc.fetchUserInfo(runtime, tokens.access_token, claims.sub);
    session = { tokens, sub: claims.sub, runtime }; return true;
  }
  const embedded = config.productionFixture ? createEmbeddedPartner(config, { clientId: () => clientId, getClient: client }) : null;
  return {
    embeddedMetrics: () => embedded?.metrics(),
    prepare(id, value) { clientId = id; if (value) secret = value; currentClient = null; },
    holdNext() { hold = true; held = null; },
    hasHeld() { return !!held; },
    async exchangeHeld() { if (!held) throw Error('No held authorization'); const value = held; held = null; return exchange(value.url, value.transaction); },
    async userInfoWorks() { if (!session) throw Error('No real Production session'); try { await oidc.fetchUserInfo(session.runtime, session.tokens.access_token, session.sub); return true; } catch { return false; } },
    async authorizationDenied() {
      const runtime = await client(), verifier = oidc.randomPKCECodeVerifier();
      const url = oidc.buildAuthorizationUrl(runtime, { redirect_uri: redirectUri, scope: 'openid', state: oidc.randomState(), nonce: oidc.randomNonce(), code_challenge_method: 'S256', code_challenge: await oidc.calculatePKCECodeChallenge(verifier) });
      const response = await fixtureFetch(url);
      if (response.status >= 400) return true;
      const location = response.headers.get('Location');
      if (!location) return false;
      const target = new URL(location, config.issuer);
      return target.origin === new URL(redirectUri).origin && target.pathname === new URL(redirectUri).pathname && target.searchParams.has('error');
    },
    async introspectionActive() {
      if (!session || !secret) return false;
      const metadata = session.runtime.serverMetadata();
      const response = await fixtureFetch(metadata.introspection_endpoint, { method: 'POST', headers: { Authorization: 'Basic ' + Buffer.from(encodeURIComponent(clientId) + ':' + encodeURIComponent(secret)).toString('base64'), 'Content-Type': 'application/x-www-form-urlencoded' }, body: new URLSearchParams({ token: session.tokens.access_token }) });
      return response.ok && (await response.json()).active === true;
    },
    async handle(req, res, url) {
      if (await embedded?.handle(req, res, url)) return true;
      if (req.method !== 'GET') return false;
      const html = text => { res.setHeader('Content-Type', 'text/html; charset=utf-8'); res.end('<!doctype html><html lang=en><meta charset=utf-8><title>Isolated Production partner</title><h1>Isolated Production partner</h1><p id=status>' + text + '</p></html>'); };
      if (url.pathname === '/production/login') {
        if (transactions.size >= 20) throw Error('Bounded fixture transactions exceeded');
        const state = oidc.randomState(), nonce = oidc.randomNonce(), verifier = oidc.randomPKCECodeVerifier();
        transactions.set(state, { state, nonce, verifier });
        const target = oidc.buildAuthorizationUrl(await client(), { redirect_uri: redirectUri, scope: 'openid', state, nonce, code_challenge_method: 'S256', code_challenge: await oidc.calculatePKCECodeChallenge(verifier), prompt: 'consent' });
        res.writeHead(302, { Location: target.href }); res.end(); return true;
      }
      if (url.pathname === '/production/callback') {
        const transaction = transactions.get(url.searchParams.get('state')); transactions.delete(url.searchParams.get('state'));
        if (!transaction) throw Error('Invalid Production state');
        if (hold) { hold = false; held = { url, transaction }; html('held_authorization'); return true; }
        try { await exchange(url, transaction); html('production_authenticated'); } catch { res.statusCode = 400; html('production_denied'); } return true;
      }
      return false;
    },
  };
}
