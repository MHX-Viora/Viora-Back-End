// Real SDK v1 and partner-owned authentication; no Social credential relay.
import { connectHostBridge, createMiniAppSdk, completePopupRedirect } from '../../../viora/packages/miniapp-sdk/src/index';
const status = document.querySelector<HTMLParagraphElement>('#status')!;
if (location.pathname === '/production/embedded-callback') {
  try { completePopupRedirect('https://partner.ankt.example.com/production/embedded-callback'); }
  catch { status.textContent = 'callback_denied'; }
} else {
try {
  const config = await (await fetch('/production-host-config.json', { cache: 'no-store', credentials: 'omit' })).json();
  const bridge = await connectHostBridge(config.hostOrigin, { protocolVersion: 1 });
  const context = await bridge.getContext();
  if (context.environment !== 'Production' || context.runtime !== 'controlled-web') throw Error('Unexpected runtime');
  document.querySelector('#context')!.textContent = `${await bridge.getEnvironment()} · inside ${await bridge.isInsideAnkt()}`;
  document.querySelector('#close')!.textContent = 'Close Production';
  document.querySelector('#close')!.addEventListener('click', () => { void bridge.close().catch(() => { status.textContent = 'close_denied'; }); });
  status.textContent = 'production_bridge_verified';
  document.body.dataset.fixtureReady = 'true';
  window.addEventListener('pagehide', () => bridge.dispose(), { once: true });
  const authConfig = await (await fetch('/production/embedded-config', { cache: 'no-store', credentials: 'omit' })).json();
  if (authConfig.clientId) {
  const negative = document.createElement('p'); negative.id = 'production-negative'; document.body.append(negative);
  const output = document.createElement('p'); output.id = 'production-auth'; output.textContent = 'unauthenticated'; document.body.append(output);
  const add = (id: string, label: string, action: () => void) => { const button = document.createElement('button'); button.id = id; button.textContent = label; button.addEventListener('click', action); document.body.append(button); };
  let stopped = false, epoch = 0, sdk: ReturnType<typeof createMiniAppSdk> | undefined;
  let popup: Window | null = null, attempt: string | undefined, timer: ReturnType<typeof setTimeout> | undefined;
  const storage = new Map<string, string>();
  const stop = () => { epoch++; clearTimeout(timer); popup?.close(); popup = null; attempt = undefined; };
  async function session() {
    const current = epoch;
    try { const response = await fetch('/production/embedded-session', { credentials: 'same-origin', cache: 'no-store', redirect: 'error', signal: AbortSignal.timeout(10000) });
      if (!response.ok) throw Error('No session');
      if (!stopped && current === epoch) output.textContent = 'production_authenticated';
    } catch { if (!stopped && current === epoch) output.textContent = 'unauthenticated'; }
  }
  function login(scope = 'openid', prompt?: 'consent') {
    stop(); const current = epoch; output.textContent = 'awaiting_authorization';
    if (authConfig.clientType === 'Public') {
      sdk?.auth.logout(); sdk = createMiniAppSdk({ issuer: authConfig.issuer, clientId: authConfig.clientId,
        redirectUri: authConfig.redirectUri, scope }, { getItem: key => storage.get(key) ?? null,
        setItem: (key, value) => { storage.set(key, value); }, removeItem: key => { storage.delete(key); } });
      const client = sdk;
      void client.auth.loginPopup({ prompt }).then(async () => { await client.auth.getUser();
        if (!stopped && current === epoch) output.textContent = 'production_authenticated';
      }).catch(() => { if (!stopped && current === epoch) output.textContent = 'production_denied'; });
    } else {
      attempt = crypto.randomUUID();
      popup = window.open('/production/embedded-login?' + new URLSearchParams({ attempt, scope, ...(prompt ? { prompt } : {}) }), '_blank', 'popup,width=520,height=720');
      if (!popup) { output.textContent = 'popup_blocked'; return; }
      timer = setTimeout(() => { if (epoch === current) { stop(); output.textContent = 'production_denied'; } }, 60000);
    }
  }
  const receive = (event: MessageEvent) => {
    const value = event.data;
    if (stopped || event.source !== popup || event.origin !== location.origin || !value || typeof value !== 'object' || Array.isArray(value) ||
      Object.keys(value).sort().join(',') !== 'attempt,channel,type,version' || value.channel !== 'ankt.partner.session' ||
      value.version !== 1 || value.type !== 'complete' || value.attempt !== attempt) return;
    stop(); void session().then(() => { if (output.textContent === 'unauthenticated') output.textContent = 'production_denied'; });
  };
  window.addEventListener('message', receive);
  add('production-login', 'Production login', () => login());
  add('production-force', 'Production consent', () => login('openid', 'consent'));
  add('production-scope', 'Production profile scope', () => login('openid profile'));
  add('production-reload', 'Production reload', () => location.reload());
  add('production-forge-host', 'Forge host completion', () => {
    window.top?.postMessage({ channel: 'ankt.production.host', version: 1, type: 'host.closed', documentNonce: 'a'.repeat(43),
      sessionId: '11111111-1111-4111-8111-111111111111', confirmed: true }, 'https://portal.ankt.localhost:5594');
    window.parent.postMessage({ channel: 'ankt.production.host', version: 1, type: 'host.stop', documentNonce: 'a'.repeat(43),
      sessionId: '11111111-1111-4111-8111-111111111111', browserNonce: 'b'.repeat(43) }, authConfig.hostOrigin);
    negative.textContent = 'forged_sent';
  });
  add('production-check-context', 'Verify context after attack', () => { void bridge.getContext().then(value => {
    negative.textContent = value.appId === context.appId && value.environment === 'Production' ? 'context_verified' : 'context_wrong';
  }).catch(() => { negative.textContent = 'context_denied'; }); });
  add('production-dangerous-link', 'Reject dangerous link', () => {
    void bridge.openExternalUrl('javascript:alert(1)').then(() => { negative.textContent = 'unexpected_navigation'; })
      .catch(() => { negative.textContent = 'dangerous_link_denied'; });
  });
  add('production-permission', 'Verify media policy denial', () => {
    const policy = (document as Document & { featurePolicy?: { allowsFeature: (name: string) => boolean } }).featurePolicy;
    if (!policy || ['camera', 'microphone', 'geolocation'].some(name => policy.allowsFeature(name))) { negative.textContent = 'policy_unverified'; return; }
    void navigator.mediaDevices.getUserMedia({ audio: true, video: true }).then(stream => {
      stream.getTracks().forEach(track => track.stop()); negative.textContent = 'unexpected_media';
    }).catch(error => { negative.textContent = error.name === 'NotAllowedError' ? 'media_policy_denied' : 'media_unverified'; });
  });
  add('production-revalidate', 'Production revalidate', () => {
    if (authConfig.clientType === 'Confidential') void session();
    else void sdk?.auth.getUser().then(() => { output.textContent = 'production_authenticated'; }).catch(() => { output.textContent = 'unauthenticated'; });
  });
  add('production-logout', 'Production local logout', () => {
    stop(); sdk?.auth.logout(); output.textContent = 'unauthenticated';
    if (authConfig.clientType === 'Confidential') void fetch('/production/embedded-logout', { method: 'POST', credentials: 'same-origin', redirect: 'error' });
  });
  if (authConfig.clientType === 'Confidential') await session();
  window.addEventListener('pagehide', () => { stopped = true; stop(); sdk?.auth.logout(); window.removeEventListener('message', receive); storage.clear(); }, { once: true });
  }
} catch { status.textContent = 'production_bridge_denied'; }
}
