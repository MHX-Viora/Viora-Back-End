import test from 'node:test';
import assert from 'node:assert/strict';
import { connectNativeParent } from '../../viora-BE/Production/Host/native.mjs';

test('native channel exists only in a top trusted shell with platform-scoped port', () => {
  const window = {}; window.parent = window.top = window;
  assert.equal(connectNativeParent(window, 'https://app.test'), null);
  window.ANKTProductionHost = { postMessage() {} };
  window.top = {};
  assert.equal(connectNativeParent(window, 'https://app.test'), null);
});
test('native messages use private parent identity, bounded JSON and disposal', () => {
  const sent = [], window = { ANKTProductionHost: { postMessage: value => sent.push(value) } };
  window.parent = window.top = window;
  const port = connectNativeParent(window, 'https://app.test'), received = [];
  port.listen(event => received.push(event));
  port.parent.postMessage({ channel: 'ankt.production.host', type: 'host.prepared' }, 'https://app.test');
  assert.equal(JSON.parse(sent[0]).type, 'host.prepared');
  const deliver = window.ANKTProductionHost.onmessage;
  for (const data of ['bad', '[]', 'x'.repeat(16385)]) deliver({ data });
  assert.equal(received.length, 0);
  deliver({ data: JSON.stringify({ type: 'host.stop' }) });
  assert.equal(received[0].source, port.parent);
  assert.equal(received[0].origin, 'https://app.test');
  port.dispose(); deliver({ data: '{}' });
  assert.equal(received.length, 1);
  assert.equal(window.ANKTProductionHost.onmessage, null);
});
test('native teardown permits exactly one final close/error receipt after listener disposal', () => {
  const sent = [], window = { ANKTProductionHost: { postMessage: value => sent.push(JSON.parse(value)) } };
  window.parent = window.top = window;
  const port = connectNativeParent(window, 'https://app.test');
  port.dispose();
  port.parent.postMessage({ type: 'host.prepared' }, 'https://app.test');
  port.parent.postMessage({ type: 'host.error' }, 'https://attacker.test');
  port.parent.postMessage({ type: 'host.error', code: 'x'.repeat(16385) }, 'https://app.test');
  port.parent.postMessage({ type: 'host.closed', confirmed: true }, 'https://app.test');
  port.dispose();
  port.parent.postMessage({ type: 'host.closed', confirmed: true }, 'https://app.test');
  port.parent.postMessage({ type: 'host.error' }, 'https://app.test');
  assert.deepEqual(sent, [{ type: 'host.closed', confirmed: true }]);
});
