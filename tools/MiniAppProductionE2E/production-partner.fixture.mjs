// Partner-owned test backend. Only real OIDC validation/UserInfo creates a session.
import { createRequire } from 'node:module';
import { pathToFileURL } from 'node:url';
import { resolve } from 'node:path';
const require = createRequire(resolve(import.meta.dirname, '../../../viora/packages/miniapp-sdk/package.json'));
const oidc = await import(pathToFileURL(require.resolve('openid-client')).href);
const { PrivateStore, readCookie, cookie } = await import('../../../viora/packages/miniapp-sdk/examples/confidential/session.mjs');
export function createEmbeddedPartner(config, { clientId, getClient }) {
  const origin = 'https://partner.ankt.example.com', redirectUri = origin + '/production/embedded-callback';
  const transactions = new PrivateStore(20), sessions = new PrivateStore(20);
  const metrics = { exchanges: 0, userInfo: 0, denied: 0 };
  const json = (response, status, value) => { response.statusCode = status; response.setHeader('Content-Type', 'application/json'); response.end(JSON.stringify(value)); };
  const completion = (response, attempt) => {
    response.setHeader('Content-Type', 'text/html; charset=utf-8');
    response.end('<!doctype html><html><meta charset=utf-8><meta name=popup-attempt content="' + attempt + '"><p id=status>revalidate_partner_session</p><script src=/production/embedded-complete.js></script></html>');
  };
  async function userInfo(session) {
    if (!session || session.expiresAt <= Date.now()) throw Error('No current partner session');
    await oidc.fetchUserInfo(session.runtime, session.tokens.access_token, session.sub); metrics.userInfo++;
  }
  return {
    metrics: () => ({ ...metrics }),
    async handle(request, response, url) {
      if (!url.pathname.startsWith('/production/embedded-') || url.pathname === '/production/embedded-callback.js') return false;
      response.setHeader('Cache-Control', 'no-store'); response.setHeader('Referrer-Policy', 'no-referrer');
      try {
        if (request.headers.host !== 'partner.ankt.example.com' || url.origin !== origin ||
            request.method !== (url.pathname === '/production/embedded-logout' ? 'POST' : 'GET')) throw Error('Invalid request');
        if (url.pathname === '/production/embedded-config') {
          json(response, 200, { issuer: config.issuer, clientId: clientId(), clientType: config.partnerClientType,
            hostOrigin: config.productionHostOrigin, redirectUri }); return true;
        }
        if (url.pathname === '/production/embedded-complete.js') {
          response.setHeader('Content-Type', 'text/javascript');
          response.end(`const attempt=document.querySelector('meta[name="popup-attempt"]')?.content;history.replaceState(null,'','/');if(window.opener&&/^[0-9a-f-]{36}$/.test(attempt??'')){window.opener.postMessage({channel:'ankt.partner.session',version:1,type:'complete',attempt},location.origin);window.close();}`); return true;
        }
        if (url.pathname === '/production/embedded-login') {
          const attempt = url.searchParams.get('attempt'), scope = url.searchParams.get('scope') ?? 'openid', prompt = url.searchParams.get('prompt');
          if (config.partnerClientType !== 'Confidential' || !/^[0-9a-f-]{36}$/.test(attempt ?? '') ||
              !['openid', 'openid profile'].includes(scope) || prompt !== null && prompt !== 'consent') throw Error('Invalid transaction');
          const runtime = await getClient(), state = oidc.randomState(), nonce = oidc.randomNonce(), verifier = oidc.randomPKCECodeVerifier();
          const previous = readCookie(request.headers.cookie, '__Host-ProductionPartner'); sessions.remove(previous);
          const key = transactions.put({ state, nonce, verifier, attempt }, 300000);
          const authorization = oidc.buildAuthorizationUrl(runtime, { redirect_uri: redirectUri, scope, state, nonce,
            code_challenge_method: 'S256', code_challenge: await oidc.calculatePKCECodeChallenge(verifier), ...(prompt ? { prompt } : {}) });
          response.setHeader('Set-Cookie', [cookie('__Host-ProductionLogin', key, 300), cookie('__Host-ProductionPartner', '', 0, 'None')]);
          response.writeHead(302, { Location: authorization.href }); response.end(); return true;
        }
        if (url.pathname === '/production/embedded-callback') {
          if (config.partnerClientType === 'Public') {
            response.setHeader('Content-Type', 'text/html; charset=utf-8');
            response.end('<!doctype html><html><meta charset=utf-8><p id=status>SDK callback</p><script type=module src=/production/embedded-callback.js></script></html>'); return true;
          }
          const transaction = transactions.take(readCookie(request.headers.cookie, '__Host-ProductionLogin'));
          if (!transaction || url.searchParams.getAll('state').length !== 1 || url.searchParams.get('state') !== transaction.state) throw Error('Invalid browser transaction');
          response.setHeader('Set-Cookie', cookie('__Host-ProductionLogin', '', 0));
          if (url.searchParams.has('error')) { metrics.denied++; completion(response, transaction.attempt); return true; }
          const runtime = await getClient();
          const tokens = await oidc.authorizationCodeGrant(runtime, url, { pkceCodeVerifier: transaction.verifier,
            expectedState: transaction.state, expectedNonce: transaction.nonce, idTokenExpected: true });
          const claims = tokens.claims();
          if (!claims?.sub || !tokens.access_token || !Number.isFinite(tokens.expires_in) || tokens.expires_in <= 0 || tokens.expires_in > 300 || tokens.refresh_token) throw Error('Invalid token');
          const session = { runtime, tokens, sub: claims.sub, expiresAt: Date.now() + tokens.expires_in * 1000 };
          await userInfo(session); metrics.exchanges++;
          const key = sessions.put(session, tokens.expires_in * 1000);
          response.setHeader('Set-Cookie', [cookie('__Host-ProductionLogin', '', 0), cookie('__Host-ProductionPartner', key, tokens.expires_in, 'None')]);
          completion(response, transaction.attempt); return true;
        }
        if (url.pathname === '/production/embedded-session') {
          try { await userInfo(sessions.get(readCookie(request.headers.cookie, '__Host-ProductionPartner'))); json(response, 200, { authenticated: true }); }
          catch { json(response, 401, { authenticated: false }); } return true;
        }
        if (url.pathname === '/production/embedded-logout') {
          if (request.headers.origin !== origin) throw Error('Invalid origin');
          sessions.remove(readCookie(request.headers.cookie, '__Host-ProductionPartner'));
          response.setHeader('Set-Cookie', cookie('__Host-ProductionPartner', '', 0, 'None')); json(response, 200, { authenticated: false }); return true;
        }
        throw Error('Unknown request');
      } catch { json(response, 400, { error: 'PARTNER_REQUEST_DENIED' }); return true; }
    },
  };
}
