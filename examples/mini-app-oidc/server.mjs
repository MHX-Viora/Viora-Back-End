import { createServer } from 'node:http';
import { randomBytes, randomUUID, timingSafeEqual } from 'node:crypto';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { discoverClient } from './oidc.mjs';

const random = () => randomBytes(32).toString('base64url');
const equal = (a, b) => typeof a === 'string' && typeof b === 'string' &&
  Buffer.byteLength(a) === Buffer.byteLength(b) && timingSafeEqual(Buffer.from(a), Buffer.from(b));
const json = (response, status, body) => { response.writeHead(status, { 'Content-Type': 'application/json' }); response.end(JSON.stringify(body)); };

export function settingsFromEnvironment(env = process.env) {
  const required = key => { if (!env[key]?.trim()) throw new Error(`${key} is required`); return env[key].trim(); };
  const issuer = new URL(required('ANKT_OIDC_ISSUER'));
  const host = new URL(required('ANKT_HOST_ORIGIN'));
  const callback = new URL(required('ANKT_REDIRECT_URI'));
  const localDevelopment = env.LOCAL_DEVELOPMENT === 'true';
  for (const url of [issuer, host, callback]) {
    if (url.username || url.password || (url.protocol !== 'https:' && !(localDevelopment && url.protocol === 'http:' && ['localhost', '127.0.0.1'].includes(url.hostname))))
      throw new Error('HTTPS is required; development HTTP is limited to loopback');
  }
  if (issuer.href !== issuer.origin + '/' || host.href !== host.origin + '/' || callback.hash || callback.search || callback.pathname !== '/auth/ankt/callback')
    throw new Error('Issuer/host must be origins; callback must end in /auth/ankt/callback without query or fragment');
  return { issuer: issuer.origin, hostOrigin: host.origin, origin: callback.origin, redirectUri: callback.href,
    clientId: required('ANKT_CLIENT_ID'), clientSecret: required('ANKT_CLIENT_SECRET'), localDevelopment };
}

export function createPartnerServer(settings, oauth, now = () => Date.now()) {
  // This runnable demonstration is single-process. Replace both maps with your
  // application's transactional database + shared session store before production.
  const sessions = new Map();
  const identities = new Map();
  const cookieName = settings.localDevelopment ? 'ankt_demo' : '__Host-ankt_demo';
  const cookie = id => `${cookieName}=${id}; Path=/; HttpOnly; Max-Age=300; ${settings.localDevelopment ? 'SameSite=Lax' : 'Secure; SameSite=None; Partitioned'}`;
  const sessionFor = request => {
    const id = (request.headers.cookie ?? '').split(';').map(x => x.trim()).find(x => x.startsWith(cookieName + '='))?.slice(cookieName.length + 1);
    const session = sessions.get(id);
    if (session && session.expiresAt <= now()) { sessions.delete(id); return null; }
    return session ? { id, session } : null;
  };
  const newSession = () => {
    for (const [id, session] of sessions) if (session.expiresAt <= now()) sessions.delete(id);
    if (sessions.size >= 10_000) throw new Error('Session capacity exceeded');
    const id = random(); const session = { csrfToken: random(), expiresAt: now() + 300_000 };
    sessions.set(id, session); return { id, session };
  };
  return createServer(async (request, response) => {
    response.setHeader('Cache-Control', 'no-store');
    response.setHeader('Referrer-Policy', 'no-referrer');
    response.setHeader('X-Content-Type-Options', 'nosniff');
    response.setHeader('Content-Security-Policy', `default-src 'self'; script-src 'self' ${settings.issuer}; connect-src 'self'; style-src 'self'; frame-ancestors ${settings.hostOrigin}; base-uri 'none'; form-action 'self'`);
    let url;
    try { url = new URL(request.url, settings.origin); } // Never trust Host/X-Forwarded-Host.
    catch { return json(response, 400, { error: 'invalid_request' }); }
    const active = sessionFor(request);
    try {
      if (request.method === 'GET' && url.pathname === '/auth/ankt/session') {
        const current = active ?? newSession();
        response.setHeader('Set-Cookie', cookie(current.id));
        return json(response, 200, { csrfToken: current.session.csrfToken, authenticated: !!current.session.subject,
          runtimeSessionId: current.session.runtimeSessionId ?? null,
          issuer: settings.issuer, hostOrigin: settings.hostOrigin });
      }
      if (request.method === 'POST' && ['/auth/ankt/start', '/auth/ankt/logout'].includes(url.pathname)) {
        if (!active || request.headers.origin !== settings.origin || !equal(request.headers['x-csrf-token'], active.session.csrfToken))
          return json(response, 403, { error: 'csrf_failed' });
        if (url.pathname.endsWith('/logout')) {
          const token = active.session.accessToken; sessions.delete(active.id);
          response.setHeader('Set-Cookie', `${cookieName}=; Path=/; HttpOnly; Max-Age=0; ${settings.localDevelopment ? 'SameSite=Lax' : 'Secure; SameSite=None; Partitioned'}`);
          let providerRevoked = true;
          try { if (token) await oauth.revoke(token); } catch { providerRevoked = false; }
          return json(response, 200, { loggedOut: true, providerRevoked });
        }
        if (active.session.transaction && active.session.transaction.expiresAt > now())
          return json(response, 409, { error: 'login_in_progress' });
        let body = '';
        for await (const chunk of request) {
          body += chunk;
          if (Buffer.byteLength(body) > 2048) return json(response, 413, { error: 'request_too_large' });
        }
        const input = JSON.parse(body || '{}');
        const runtimeSessionId = input.runtimeSessionId;
        if (runtimeSessionId !== undefined && (typeof runtimeSessionId !== 'string' || !/^[A-Za-z0-9_-]{1,80}$/.test(runtimeSessionId) || typeof input.appId !== 'string' || !/^[A-Za-z0-9_-]{1,80}$/.test(input.appId)))
          return json(response, 400, { error: 'invalid_runtime_binding' });
        if (runtimeSessionId && active.session.runtimeSessionId !== runtimeSessionId) {
          // Never keep serving the previous partner account during an account
          // switch, even if the new OAuth flow is denied or its callback fails.
          delete active.session.subject; delete active.session.localAccountId;
          delete active.session.accessToken; delete active.session.runtimeSessionId;
        }
        active.session.transaction = { expiresAt: now() + 300_000 }; // Lock concurrent starts.
        let transaction;
        try { transaction = await oauth.start(); }
        catch (error) { delete active.session.transaction; throw error; }
        active.session.transaction = { ...transaction, runtimeSessionId, expiresAt: now() + 300_000 };
        return json(response, 200, { authorizeUrl: transaction.authorizeUrl, sdkParams: {
          clientId: settings.clientId, redirectUri: settings.redirectUri, state: transaction.state, nonce: transaction.nonce,
          codeChallenge: transaction.challenge, codeChallengeMethod: 'S256', scopes: ['openid', 'profile'],
        } });
      }
      if (request.method === 'GET' && url.pathname === '/auth/ankt/callback') {
        const transaction = active?.session.transaction;
        if (active) delete active.session.transaction; // Consume before any asynchronous work.
        if (!transaction || transaction.expiresAt <= now() || url.searchParams.getAll('state').length !== 1 || !equal(url.searchParams.get('state'), transaction.state))
          return json(response, 400, { error: 'invalid_or_expired_transaction' });
        const callback = new URL(settings.redirectUri); callback.search = url.search;
        const tokens = await oauth.complete(callback, transaction);
        const claims = tokens.claims();
        if (!claims?.sub || !tokens.access_token || !Number.isFinite(tokens.expires_in) || tokens.expires_in <= 0)
          throw new Error('Invalid identity response');
        // Only the validated signed ID token can attest the ANKT runtime.
        // The browser-supplied ID alone must never establish an authenticated session.
        if (transaction.runtimeSessionId && claims.runtime_session_id !== transaction.runtimeSessionId)
          throw new Error('Runtime mismatch');
        const identityKey = `${settings.issuer}\n${claims.sub}`;
        // Unique (issuer, pairwise subject), never match/link accounts by email.
        if (!identities.has(identityKey)) identities.set(identityKey, randomUUID());
        const current = newSession(); sessions.delete(active.id);
        Object.assign(current.session, { subject: claims.sub, localAccountId: identities.get(identityKey),
          runtimeSessionId: transaction.runtimeSessionId ?? null,
          accessToken: tokens.access_token, expiresAt: now() + Math.min(300, tokens.expires_in) * 1000 });
        response.writeHead(303, { Location: '/', 'Set-Cookie': cookie(current.id) }); return response.end();
      }
      if (request.method === 'GET' && url.pathname === '/auth/ankt/profile') {
        if (!active?.session.subject) return json(response, 401, { error: 'login_required' });
        // Check the provider on each protected action; revoked tokens/permissions
        // must stop access even while a local cookie has not yet expired.
        const profile = await oauth.profile(active.session.accessToken, active.session.subject);
        if (profile.sub !== active.session.subject) throw new Error('Subject mismatch');
        return json(response, 200, { accountId: active.session.localAccountId, sub: profile.sub,
          ...(typeof profile.name === 'string' ? { name: profile.name } : {}),
          ...(typeof profile.picture === 'string' ? { picture: profile.picture } : {}) });
      }
      if (request.method === 'GET' && ['/', '/client.mjs', '/style.css'].includes(url.pathname)) {
        const file = url.pathname === '/' ? 'index.html' : url.pathname.slice(1);
        response.writeHead(200, { 'Content-Type': file.endsWith('.mjs') ? 'text/javascript' : file.endsWith('.css') ? 'text/css' : 'text/html; charset=utf-8' });
        return response.end(await readFile(new URL(file, import.meta.url)));
      }
      if (request.method === 'GET' && url.pathname === '/favicon.ico') { response.writeHead(204); return response.end(); }
      return json(response, 404, { error: 'not_found' });
    } catch {
      if (active?.session.subject && url.pathname === '/auth/ankt/profile') {
        sessions.delete(active.id); return json(response, 401, { error: 'session_invalid' });
      }
      // No request URLs, credentials, callback codes, tokens or PII in logs/errors.
      return json(response, 400, { error: 'authorization_failed' });
    }
  });
}

if (process.argv[1] && fileURLToPath(import.meta.url) === process.argv[1]) {
  const settings = settingsFromEnvironment();
  const oauth = await discoverClient(settings);
  const server = createPartnerServer(settings, oauth);
  server.listen(Number(process.env.PORT || 4310), '127.0.0.1', () => console.log('Partner OIDC demo listening on loopback. Use your HTTPS reverse proxy.'));
}
