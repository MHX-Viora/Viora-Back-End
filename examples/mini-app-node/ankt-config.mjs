// Public platform addresses; credentials are read only by the partner backend.
export function loadAnktConfig(env) {
  const callback = env.ANKT_REDIRECT_URI?.trim() || `${(env.MINI_APP_ORIGIN || 'http://127.0.0.1:5310').replace(/\/$/, '')}/auth/ankt/callback`;
  const production = env.NODE_ENV ? env.NODE_ENV === 'production' : callback.startsWith('https:');
  if (production && !env.ANKT_REDIRECT_URI && !env.MINI_APP_ORIGIN) throw new Error('Configure ANKT_REDIRECT_URI');
  function url(value, name) {
    let parsed;
    try { parsed = new URL(value); } catch { throw new Error(`Invalid ${name}`); }
    if (parsed.username || parsed.password || (parsed.protocol !== 'https:' && (production || parsed.protocol !== 'http:' || !['127.0.0.1', 'localhost'].includes(parsed.hostname)))) throw new Error(`${name} requires HTTPS outside localhost development`);
    return parsed;
  }
  const redirect = url(callback, 'ANKT_REDIRECT_URI');
  if (redirect.search || redirect.hash || ['/', '/login', '/register', '/logout', '/auth/ankt/start', '/account/unlink'].includes(redirect.pathname)) throw new Error('ANKT_REDIRECT_URI requires a dedicated callback path without query or fragment');
  const origin = new URL(redirect.origin);
  const anktApi = url(env.ANKT_API_URL || 'https://api.mxh.ankt.vn', 'ANKT_API_URL');
  const host = url(env.ANKT_APP_ORIGIN || 'https://app.ankt.vn', 'ANKT_APP_ORIGIN');
  for (const endpoint of [anktApi, host]) {
    if (endpoint.pathname !== '/' || endpoint.search || endpoint.hash) throw new Error('ANKT endpoint configuration must be an origin');
  }
  const clientId = env.ANKT_CLIENT_ID?.trim();
  if (!clientId) throw new Error('Configure ANKT_CLIENT_ID; confidential clients also require ANKT_CLIENT_SECRET');
  return { production, origin, anktApi, anktHost: host.origin, callback, callbackPath: redirect.pathname, clientId, clientSecret: env.ANKT_CLIENT_SECRET };
}
