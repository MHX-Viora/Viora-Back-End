import { createProductionBridge } from './bridge.mjs';
import { createExternalOpener } from './navigation.mjs';
import { connectNativeParent } from './native.mjs';
export const PRIVATE_CHANNEL = 'ankt.production.host';
const record = value => value !== null && typeof value === 'object' && !Array.isArray(value);
const nonce = value => typeof value === 'string' && /^[A-Za-z0-9_-]{43}$/.test(value);
const uuid = value => typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
export function isParentMessage(event, parent, origin, generation, type, fields) {
  const value = event.data, keys = ['channel', 'version', 'type', 'documentNonce', ...fields];
  return !!parent && event.source === parent && event.origin === origin && record(value) &&
    Object.keys(value).length === keys.length && keys.every(key => Object.hasOwn(value, key)) &&
    value.channel === PRIVATE_CHANNEL && value.version === 1 && value.type === type && value.documentNonce === generation;
}
export function partnerTarget(config) {
  const url = new URL(config.websiteUrl);
  if (url.protocol !== 'https:' || url.username || url.password || url.hash || url.origin !== config.partnerOrigin || url.origin === config.hostOrigin)
    throw new Error('Invalid partner target');
  return url;
}

export function startProductionHost({ window, document, fetch, config }) {
  const status = document.querySelector('#status'), preview = document.querySelector('#preview');
  const native = connectNativeParent(window, config.parentOrigin);
  const parent = window.parent !== window ? window.parent : native?.parent;
  const bytes = window.crypto.getRandomValues(new Uint8Array(32));
  const generation = window.btoa(String.fromCharCode(...bytes)).replaceAll('+', '-').replaceAll('/', '_').replaceAll('=', '');
  const controller = new AbortController(); let stopped = false, closing = false, navigating = false, frame, bridge, timer, heartbeat, expiry, closeTimer;
  let browserNonce, sessionId = config.sessionId, pending = false, visibilityGeneration = 0, openExternal, frameLoaded;
  const closeButton = document.querySelector('#close'), externalButton = document.querySelector('#external');
  const notify = (type, fields = {}) => parent?.postMessage({ channel: PRIVATE_CHANNEL, version: 1, type, documentNonce: generation, ...fields }, config.parentOrigin);
  const request = async (path, body, terminal = false, signal) => {
    const headers = { Accept: 'application/json', ...(body === undefined ? {} : { 'Content-Type': 'application/json' }) };
    if (sessionId && browserNonce) { headers['X-Ankt-Host-Session'] = sessionId; headers['X-Ankt-Host-Nonce'] = browserNonce; }
    const response = await fetch(path, { method: body === undefined ? 'GET' : 'POST', headers, credentials: 'same-origin', cache: 'no-store', redirect: 'error',
      body: body === undefined ? undefined : JSON.stringify(body), keepalive: terminal,
      signal: terminal ? AbortSignal.timeout(3000) : AbortSignal.any([controller.signal, AbortSignal.timeout(10000), ...(signal ? [signal] : [])]) });
    if (!response.ok) throw Object.assign(new Error('Host request unavailable'), { status: response.status });
    return response.status === 204 ? null : response.json();
  };
  function dispose() {
    stopped = true; controller.abort(); bridge?.dispose(); clearTimeout(timer); clearTimeout(expiry); clearTimeout(closeTimer); clearInterval(heartbeat);
    if (frameLoaded) frame?.removeEventListener('load', frameLoaded);
    frame?.remove(); window.removeEventListener('message', receive); native?.dispose();
    window.removeEventListener('pagehide', onPageHide); document.removeEventListener('visibilitychange', visibilityChanged);
    closeButton.removeEventListener('click', closeClicked); visibilityGeneration++;
    externalButton.removeEventListener('click', externalClicked); externalButton.disabled = true;
  }
  function fail(code = 'HOST_UNAVAILABLE') {
    if (stopped) return;
    dispose(); status.textContent = 'Không thể xác nhận phiên Mini App. Vui lòng mở lại từ ANKT.';
    notify('host.error', { sessionId: sessionId ?? null, code });
  }
  async function close() {
    if (closing) return; closing = true; dispose(); let confirmed = !sessionId;
    if (sessionId && browserNonce) { try { await request('/api/mini-app-host/close', {}, true); confirmed = true; } catch { confirmed = false; } }
    status.textContent = confirmed ? 'Đã đóng Mini App.' : 'Đã dừng hiển thị. Chưa xác nhận đóng phiên; phiên sẽ tự hết hạn.';
    notify('host.closed', { sessionId: sessionId ?? null, confirmed });
  }
  async function mount() {
    const context = await request('/api/mini-app-host/context');
    if (stopped) return;
    if (!record(context) || context.appId !== config.appId || context.environment !== 'Production' || context.runtime !== 'controlled-web') throw new Error('Invalid context');
    const url = partnerTarget(config);
    document.querySelector('#app-name').textContent = config.name;
    frame = document.createElement('iframe'); frame.title = config.name; frame.referrerPolicy = 'no-referrer';
    frame.setAttribute('sandbox', 'allow-scripts allow-same-origin allow-forms');
    frame.setAttribute('allow', "camera 'none'; microphone 'none'; geolocation 'none'; payment 'none'; usb 'none'");
    let loaded = false, reported = false;
    const waitForSdk = () => {
      clearTimeout(timer);
      timer = setTimeout(() => { if (!stopped && !bridge.connected) { status.textContent = 'Chưa xác nhận kết nối SDK. Website có thể chặn nhúng hoặc chưa tích hợp SDK.'; notify('bridge.unavailable', { sessionId }); } }, 15000);
    };
    bridge = createProductionBridge({ origin: config.partnerOrigin, source: () => frame?.contentWindow, appId: config.appId,
      validate: signal => request('/api/mini-app-host/context', undefined, false, signal),
      send: message => frame?.contentWindow?.postMessage(message, config.partnerOrigin), close: () => request('/api/mini-app-host/close', {}, true),
      onInvalidated: () => fail('SESSION_ENDED'),
      onConnected: () => {
        clearTimeout(timer); status.textContent = 'Đã kết nối SDK · Production'; notify('bridge.connected', { sessionId });
        if (!reported) { reported = true; void request('/api/mini-app-host/events', { code: 'BRIDGE_READY' }).catch(() => {}); }
      },
      onClosed: () => {
        closing = true;
        closeTimer = setTimeout(() => { dispose(); status.textContent = 'Đã đóng Mini App.'; notify('host.closed', { sessionId, confirmed: true }); }, 50);
      }
    });
    if (document.visibilityState !== 'visible') bridge.pause();
    frameLoaded = () => {
      if (stopped) return;
      if (loaded) { bridge.reset(); reported = false; waitForSdk(); notify('bridge.disconnected', { sessionId }); }
      loaded = true;
      if (!bridge.connected) status.textContent = 'Website đã tải. Chưa xác nhận kết nối SDK.';
      notify('website.loaded', { sessionId });
    };
    frame.addEventListener('load', frameLoaded);
    waitForSdk();
    frame.src = url.href; preview.append(frame); status.textContent = 'Đang tải website. Chưa xác nhận kết nối SDK.';
    notify('host.running', { sessionId });
    openExternal = createExternalOpener({ document, request: () => request('/api/mini-app-host/external-link', {}),
      expectedUrl: config.websiteUrl, hostOrigin: config.hostOrigin, parentOrigin: config.parentOrigin, isStopped: () => stopped });
    externalButton.disabled = false;
    heartbeat = setInterval(() => { if (!stopped && document.visibilityState === 'visible') void request('/api/mini-app-host/heartbeat', {}).catch(() => fail('SESSION_ENDED')); }, 15000);
    const remaining = Date.parse(config.expiresAt) - Date.now();
    if (!Number.isFinite(remaining) || remaining <= 0) { fail('SESSION_ENDED'); return; }
    expiry = setTimeout(() => fail('SESSION_ENDED'), Math.min(remaining, 30 * 60 * 1000));
  }
  function receive(event) {
    if (stopped || !parent) return;
    if (isParentMessage(event, parent, config.parentOrigin, generation, 'host.stop', ['sessionId', 'browserNonce']) &&
        event.data.sessionId === sessionId && nonce(event.data.browserNonce)) { browserNonce = event.data.browserNonce; void close(); return; }
    if (bridge) void bridge.handle(event);
    if (pending) return;
    if (config.mode === 'prepare' && isParentMessage(event, parent, config.parentOrigin, generation, 'host.launch', ['sessionId', 'grant', 'browserNonce'])) {
      const value = event.data;
      if (!uuid(value.sessionId) || !nonce(value.grant) || value.browserNonce !== browserNonce) return;
      pending = true; sessionId = value.sessionId; clearTimeout(timer);
      void request('/api/mini-app-host/redeem', { grant: value.grant, browserNonce }).then(result => {
        if (stopped) return;
        if (result?.sessionId !== sessionId) throw new Error('Invalid redemption');
        navigating = true; window.location.replace('/mini-app-host/run/' + sessionId);
      }).catch(() => fail());
    } else if (config.mode === 'run' && isParentMessage(event, parent, config.parentOrigin, generation, 'host.resume', ['sessionId', 'browserNonce'])) {
      if (event.data.sessionId !== sessionId || !nonce(event.data.browserNonce)) return;
      pending = true; browserNonce = event.data.browserNonce; clearTimeout(timer); void mount().catch(() => fail());
    }
  }
  window.addEventListener('message', receive);
  native?.listen(receive);
  function onPageHide() { if (!navigating) void close(); else dispose(); }
  function closeClicked() { void close(); }
  async function externalClicked(event) {
    if (!event.isTrusted || stopped || !openExternal) return; externalButton.disabled = true;
    try { if (await openExternal(controller.signal)) status.textContent = 'Đã chọn mở website ở tab mới.'; }
    catch { if (!stopped) status.textContent = 'Không thể xác nhận liên kết ngoài. Vui lòng thử lại.'; }
    finally { if (!stopped) externalButton.disabled = false; }
  }
  function visibilityChanged() {
    if (stopped || !bridge) return; const current = ++visibilityGeneration;
    if (document.visibilityState !== 'visible') { bridge.pause(); notify('host.paused', { sessionId }); return; }
    void request('/api/mini-app-host/context').then(context => {
      if (stopped || current !== visibilityGeneration || document.visibilityState !== 'visible') return;
      if (context?.appId !== config.appId || context.environment !== 'Production' || context.runtime !== 'controlled-web') throw new Error('Invalid context');
      bridge.resume(); notify('host.resumed', { sessionId });
    }).catch(() => fail('SESSION_ENDED'));
  }
  window.addEventListener('pagehide', onPageHide, { once: true });
  document.addEventListener('visibilitychange', visibilityChanged); closeButton.addEventListener('click', closeClicked);
  externalButton.addEventListener('click', externalClicked);
  async function initialize() {
    if (!parent || window.location.origin !== config.hostOrigin || window.location.hash || !['prepare', 'run'].includes(config.mode)) throw new Error('Invalid host document');
    if (config.mode === 'prepare') {
      const prepared = await request('/api/mini-app-host/prepare'); if (stopped) return;
      if (!nonce(prepared?.browserNonce) || typeof prepared.bindingId !== 'string' || prepared.bindingId.length > 4096) throw new Error('Invalid preparation');
      browserNonce = prepared.browserNonce; status.textContent = 'Đang chờ ANKT cho phép mở Mini App…';
      notify('host.prepared', { bindingId: prepared.bindingId, browserNonce, expiresAt: prepared.expiresAt });
    } else {
      if (!uuid(sessionId)) throw new Error('Invalid session');
      status.textContent = 'Đang xác minh phiên…'; notify('host.runtime.ready', { sessionId });
    }
    timer = setTimeout(() => fail(), 120000);
  }
  void initialize().catch(() => fail());
  return { close, dispose };
}

if (typeof window !== 'undefined' && typeof document !== 'undefined') {
  try { startProductionHost({ window, document, fetch: window.fetch.bind(window), config: JSON.parse(document.querySelector('#host').getAttribute('data-config')) }); }
  catch { document.querySelector('#status').textContent = 'Không thể khởi tạo Mini App. Vui lòng mở lại từ ANKT.'; }
}
