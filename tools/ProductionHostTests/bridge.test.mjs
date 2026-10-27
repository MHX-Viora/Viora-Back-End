import assert from 'node:assert/strict';
import test from 'node:test';
import { createProductionBridge } from '../../viora-BE/Production/Host/bridge.mjs';

let sequence = 0;
const id = () => `11111111-1111-4111-8111-${String(++sequence).padStart(12, '0')}`;
function fixture(overrides = {}) {
  const source = {}, sent = []; let validated = 0, closed = 0, invalidated = 0;
  const options = { origin: 'https://partner.example.com', source: () => source, appId: 'app_' + 'a'.repeat(43),
    validate: async () => { validated++; return { appId: options.appId, environment: 'Production', runtime: 'controlled-web' }; },
    send: message => sent.push(message), close: async () => { closed++; }, onInvalidated: () => { invalidated++; }, ...overrides };
  const bridge = createProductionBridge(options);
  const event = (method, extra = {}) => ({ source, origin: options.origin, data: { channel: 'ankt.miniapp', version: 1, type: 'request', id: id(), method, ...extra } });
  return { bridge, source, sent, event, stats: () => ({ validated, closed, invalidated }) };
}
test('v1 handshake exposes only context/close and context comes from validated Production session', async () => {
  const f = fixture(); await f.bridge.handle(f.event('platform.getContext'));
  assert.equal(f.sent.at(-1).error.code, 'BRIDGE_FORBIDDEN');
  await f.bridge.handle(f.event('bridge.handshake'));
  assert.deepEqual(f.sent.at(-1).result, { permissions: ['platform.getContext', 'platform.close'] });
  await f.bridge.handle(f.event('platform.getContext'));
  assert.deepEqual(f.sent.at(-1).result, { appId: 'app_' + 'a'.repeat(43), environment: 'Production', runtime: 'controlled-web' });
  for (const method of ['auth.getToken', 'device.camera', 'diagnostics.report', 'platform.openExternalUrl', 'platform.getEnvironment']) {
    await f.bridge.handle(f.event(method)); assert.equal(f.sent.at(-1).error.code, 'UNSUPPORTED_METHOD');
  }
  await f.bridge.handle(f.event('platform.close')); assert.equal(f.sent.at(-1).result, null); assert.equal(f.stats().closed, 1);
});
test('forged source/origin, replay, extra fields and unsupported protocol cannot acquire authority', async () => {
  const f = fixture(); const event = f.event('bridge.handshake');
  await f.bridge.handle({ ...event, source: {} }); await f.bridge.handle({ ...event, origin: 'https://evil.example.com' });
  assert.equal(f.sent.length, 0); assert.equal(f.stats().validated, 0);
  await f.bridge.handle(event); await f.bridge.handle(event); assert.equal(f.sent.at(-1).error.code, 'BRIDGE_PROTOCOL');
  await f.bridge.handle(f.event('bridge.handshake', { actor: 'forged' })); assert.equal(f.sent.at(-1).error.code, 'BRIDGE_PROTOCOL');
  await f.bridge.handle(f.event('bridge.handshake', { version: 2 })); assert.equal(f.sent.at(-1).ok, false);
});
test('reset/pause cancels stale replies, resume needs live validation and close failures stop authority', async () => {
  let release; const f = fixture({ validate: () => new Promise(resolve => { release = resolve; }) });
  const pending = f.bridge.handle(f.event('bridge.handshake')); f.bridge.reset();
  release({ appId: 'app_' + 'a'.repeat(43), environment: 'Production', runtime: 'controlled-web' }); await pending;
  assert.equal(f.sent.length, 0);
  const active = fixture(); await active.bridge.handle(active.event('bridge.handshake'));
  active.bridge.pause(); await active.bridge.handle(active.event('platform.getContext')); assert.equal(active.sent.at(-1).ok, false);
  active.bridge.resume(); await active.bridge.handle(active.event('platform.getContext')); assert.equal(active.sent.at(-1).ok, true);
  const failed = fixture({ close: async () => { throw new Error('provider details must not leak'); } });
  await failed.bridge.handle(failed.event('bridge.handshake')); await failed.bridge.handle(failed.event('platform.close'));
  assert.equal(failed.sent.at(-1).error.code, 'BRIDGE_FORBIDDEN'); assert.equal(failed.stats().invalidated, 1);
  assert.equal(JSON.stringify(failed.sent).includes('provider details'), false);
});
test('Sandbox context and request floods are rejected', async () => {
  const invalid = fixture({ validate: async () => ({ appId: 'app_' + 'a'.repeat(43), environment: 'Sandbox', runtime: 'controlled-web' }) });
  await invalid.bridge.handle(invalid.event('bridge.handshake')); assert.equal(invalid.sent.at(-1).ok, false);
  const f = fixture(); await f.bridge.handle(f.event('bridge.handshake'));
  for (let i = 0; i < 64; i++) await f.bridge.handle(f.event('platform.getContext'));
  assert.equal(f.sent.at(-1).error.code, 'BRIDGE_BUSY');
});
