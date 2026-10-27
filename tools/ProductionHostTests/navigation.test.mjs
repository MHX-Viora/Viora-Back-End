import assert from 'node:assert/strict';
import test from 'node:test';
import { externalApproval, createExternalOpener } from '../../viora-BE/Production/Host/navigation.mjs';

const expected = 'https://partner.example.com/app', settings = { hostOrigin: 'https://host.example.com', parentOrigin: 'https://app.example.com', now: () => 0 };
const approved = { url: expected, expiresAt: new Date(10_000).toISOString() };
test('external approval rejects schemes, credentials, changed targets and expired responses', () => {
  assert.equal(externalApproval(approved, expected, settings).url, expected);
  for (const url of ['javascript:alert(1)', 'http://partner.example.com/app', 'https://partner.example.com.evil.test/app', 'https://user@partner.example.com/app'])
    assert.throws(() => externalApproval({ ...approved, url }, expected, settings));
  assert.throws(() => externalApproval({ ...approved, expiresAt: new Date(-1).toISOString() }, expected, settings));
  assert.throws(() => externalApproval(approved, expected, { ...settings, hostOrigin: 'https://partner.example.com' }));
});
function documentFixture() {
  const doc = { activeElement: { isConnected: true, focus() { this.focused = true; } } };
  doc.createElement = tag => ({ tag, children: [], handlers: new Map(), isConnected: true,
    append(...children) { this.children.push(...children); }, setAttribute(name, value) { this[name] = value; },
    addEventListener(name, callback) { this.handlers.set(name, callback); }, removeEventListener(name) { this.handlers.delete(name); },
    showModal() {}, close() {}, remove() { this.isConnected = false; }, focus() {},
    fire(name, event = {}) { this.handlers.get(name)?.({ preventDefault() { this.prevented = true; }, ...event }); }
  });
  doc.body = { append(dialog) { doc.dialog = dialog; } }; return doc;
}
test('only trusted explicit confirmation follows an isolated noreferrer link; cancellation cleans modal', async () => {
  const doc = documentFixture(), controller = new AbortController(); let requested = 0;
  const open = createExternalOpener({ document: doc, request: async () => { requested++; return approved; }, expectedUrl: expected, ...settings, isStopped: () => false });
  const pending = open(controller.signal); await Promise.resolve();
  const link = doc.dialog.children.find(x => x.tag === 'a');
  assert.equal(link.href, expected); assert.equal(link.target, '_blank'); assert.equal(link.rel, 'noopener noreferrer');
  link.fire('click', { isTrusted: false }); assert.equal(await pending, false); assert.equal(requested, 1); assert.equal(doc.dialog.isConnected, false);
  const next = open(controller.signal); await Promise.resolve();
  doc.dialog.children.find(x => x.tag === 'a').fire('click', { isTrusted: true }); assert.equal(await next, true);
  const canceled = open(controller.signal); await Promise.resolve(); controller.abort();
  assert.equal(await canceled, false); assert.equal(doc.dialog.isConnected, false);
});
