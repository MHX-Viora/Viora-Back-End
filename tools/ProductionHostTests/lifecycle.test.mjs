import assert from 'node:assert/strict';
import test from 'node:test';
import { webcrypto } from 'node:crypto';
import { startProductionHost } from '../../viora-BE/Production/Host/host.mjs';

const sessionId = '11111111-1111-4111-8111-111111111111', browserNonce = 'b'.repeat(43), grant = 'g'.repeat(43);
const flush = () => new Promise(setImmediate);
function target() {
  const listeners = new Map();
  return { listeners, addEventListener(name, fn) { if (!listeners.has(name)) listeners.set(name, new Set()); listeners.get(name).add(fn); },
    removeEventListener(name, fn) { listeners.get(name)?.delete(fn); },
    fire(name, event) { for (const fn of [...(listeners.get(name) ?? [])]) fn(event); }, count() { return [...listeners.values()].reduce((n, entries) => n + entries.size, 0); } };
}
function fixture(mode = 'run', nativeMode = false) {
  const sent = [], calls = [], frames = [], navigation = [], state = { contextDenied: false };
  const parent = { postMessage(data, origin) { sent.push({ data, origin }); } };
  const window = { ...target(), parent, crypto: webcrypto, btoa: globalThis.btoa,
    location: { origin: 'https://host.example.com', hash: '', replace(url) { navigation.push(url); } } };
  const nodes = new Map(['status', 'preview', 'app-name', 'close', 'external'].map(key => [key, { ...target(), textContent: '', disabled: false, append(frame) { frames.push(frame); } }]));
  const document = { ...target(), visibilityState: 'visible', querySelector: key => nodes.get(key.slice(1)), createElement(tag) {
    assert.equal(tag, 'iframe'); return { ...target(), contentWindow: { sent: [], postMessage(message) { this.sent.push(message); } }, setAttribute() {}, remove() { this.removed = true; } };
  } };
  const config = { mode, hostOrigin: window.location.origin, parentOrigin: 'https://app.example.com', sessionId,
    appId: 'app_' + 'a'.repeat(43), name: 'Ứng dụng', websiteUrl: 'https://partner.example.com/app', partnerOrigin: 'https://partner.example.com', ssoEnabled: false,
    expiresAt: new Date(Date.now() + 60000).toISOString() };
  const fetch = async (path, options) => {
    calls.push({ path, options });
    if (path.endsWith('/prepare')) return Response.json({ bindingId: 'ticket', browserNonce, expiresAt: config.expiresAt });
    if (path.endsWith('/redeem')) return Response.json({ sessionId });
    if (path.endsWith('/context')) return state.contextDenied ? new Response(null, { status: 403 }) : Response.json({ appId: config.appId, environment: 'Production', runtime: 'controlled-web' });
    return new Response(null, { status: 204 });
  };
  if (nativeMode) {
    window.parent = window.top = window;
    window.ANKTProductionHost = { postMessage(value) { sent.push({ data: JSON.parse(value) }); } };
  }
  const host = startProductionHost({ window, document, config, fetch });
  return { host, window, document, parent, sent, calls, frames, nodes, navigation, config, state };
}
test('no partner mounts before exact private resume; dispose removes all listeners and frames', async () => {
  const f = fixture(); await flush(); const ready = f.sent.find(x => x.data.type === 'host.runtime.ready').data;
  assert.equal(f.frames.length, 0);
  const data = { channel: ready.channel, version: 1, type: 'host.resume', documentNonce: ready.documentNonce, sessionId, browserNonce };
  f.window.fire('message', { data, source: {}, origin: f.config.parentOrigin }); await flush(); assert.equal(f.frames.length, 0);
  f.window.fire('message', { data, source: f.parent, origin: f.config.parentOrigin }); await flush(); assert.equal(f.frames.length, 1);
  f.host.dispose(); assert.equal(f.frames[0].removed, true);
  assert.equal(f.window.count(), 0); assert.equal(f.document.count(), 0); assert.equal(f.nodes.get('close').count(), 0);
});
test('grant travels only in redeem POST and never in navigation URL', async () => {
  const f = fixture('prepare'); await flush(); const ready = f.sent.find(x => x.data.type === 'host.prepared').data;
  f.window.fire('message', { origin: f.config.parentOrigin, source: f.parent, data: { channel: ready.channel, version: 1, type: 'host.launch',
    documentNonce: ready.documentNonce, sessionId, browserNonce, grant } }); await flush();
  assert.deepEqual(f.navigation, ['/mini-app-host/run/' + sessionId]);
  assert.equal(f.navigation.some(url => url.includes(grant) || url.includes(browserNonce)), false);
  assert.equal(JSON.parse(f.calls.find(x => x.path.endsWith('/redeem')).options.body).grant, grant); f.host.dispose();
});
test('close before private nonce cannot claim server closure was confirmed', async () => {
  const f = fixture(); await flush(); await f.host.close();
  assert.equal(f.sent.find(x => x.data.type === 'host.closed').data.confirmed, false);
});

test('native shell sends confirmed close after scoped server cleanup and listener disposal', async () => {
  const f = fixture('run', true);
  try {
    await flush();
    const ready = f.sent.find(x => x.data.type === 'host.runtime.ready').data;
    f.window.ANKTProductionHost.onmessage({ data: JSON.stringify({ channel: ready.channel,
      version: 1, type: 'host.resume', documentNonce: ready.documentNonce, sessionId, browserNonce }) });
    await flush();
    assert.equal(f.frames.length, 1);
    await f.host.close();
    assert.equal(f.calls.filter(x => x.path.endsWith('/close')).length, 1);
    assert.equal(f.sent.filter(x => x.data.type === 'host.closed').length, 1);
    assert.equal(f.sent.at(-1).data.confirmed, true);
    assert.equal(f.frames[0].removed, true);
    assert.equal(f.window.ANKTProductionHost.onmessage, null);
    assert.equal(f.window.count(), 0);
    assert.equal(f.document.count(), 0);
    await f.host.close();
    assert.equal(f.sent.filter(x => x.data.type === 'host.closed').length, 1);
  } finally { f.host.dispose(); }
});
test('background pauses bridge actions and foreground rechecks live authority before resuming', async () => {
  const f = fixture(); await flush(); const ready = f.sent.find(x => x.data.type === 'host.runtime.ready').data;
  f.window.fire('message', { source: f.parent, origin: f.config.parentOrigin, data: { channel: ready.channel, version: 1,
    type: 'host.resume', documentNonce: ready.documentNonce, sessionId, browserNonce } }); await flush();
  const frame = f.frames[0];
  const request = (method, id) => ({ source: frame.contentWindow, origin: f.config.partnerOrigin, data: { channel: 'ankt.miniapp', version: 1, type: 'request', id, method } });
  f.window.fire('message', request('bridge.handshake', '22222222-2222-4222-8222-222222222222')); await flush();
  assert.equal(frame.contentWindow.sent.at(-1).ok, true);
  f.document.visibilityState = 'hidden'; f.document.fire('visibilitychange');
  const before = f.calls.filter(x => x.path.endsWith('/context')).length;
  f.window.fire('message', request('platform.getContext', '33333333-3333-4333-8333-333333333333')); await flush();
  assert.equal(frame.contentWindow.sent.at(-1).ok, false); assert.equal(f.calls.filter(x => x.path.endsWith('/context')).length, before);
  f.state.contextDenied = true; f.document.visibilityState = 'visible'; f.document.fire('visibilitychange'); await flush();
  assert.equal(frame.removed, true); assert.equal(f.sent.at(-1).data.type, 'host.error'); assert.equal(f.window.count(), 0);
});
