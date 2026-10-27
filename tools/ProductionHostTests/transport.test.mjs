import assert from 'node:assert/strict';
import test from 'node:test';
import { isParentMessage, partnerTarget } from '../../viora-BE/Production/Host/host.mjs';

test('private transport rejects forged parent/origin/generation, extra fields and malformed envelopes', () => {
  const parent = {}, nonce = 'a'.repeat(43), fields = ['sessionId', 'browserNonce'];
  const data = { channel: 'ankt.production.host', version: 1, type: 'host.resume', documentNonce: nonce, sessionId: 'session', browserNonce: 'b'.repeat(43) };
  const event = { origin: 'https://app.example.com', source: parent, data };
  const valid = event => isParentMessage(event, parent, 'https://app.example.com', nonce, 'host.resume', fields);
  assert.equal(valid(event), true);
  for (const forged of [
    { ...event, source: {} }, { ...event, origin: 'https://app.example.com.evil.test' },
    { ...event, data: { ...data, documentNonce: 'c'.repeat(43) } }, { ...event, data: { ...data, version: 2 } },
    { ...event, data: { ...data, token: 'forged' } }, { ...event, data: [] },
  ]) assert.equal(valid(forged), false);
});
test('partner frame URL must match the exact server-selected HTTPS origin', () => {
  const config = { hostOrigin: 'https://host.example.com', partnerOrigin: 'https://partner.example.com', websiteUrl: 'https://partner.example.com/app' };
  assert.equal(partnerTarget(config).href, config.websiteUrl);
  for (const websiteUrl of ['javascript:alert(1)', 'http://partner.example.com', 'https://partner.example.com.evil.test', 'https://user@partner.example.com', 'https://partner.example.com:444', 'https://partner.example.com/#grant'])
    assert.throws(() => partnerTarget({ ...config, websiteUrl }));
  assert.throws(() => partnerTarget({ ...config, hostOrigin: config.partnerOrigin }));
});
