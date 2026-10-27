import test from 'node:test';
import assert from 'node:assert/strict';
import { createEmbeddedPartner } from './production-partner.fixture.mjs';
function fixture(type = 'Confidential') {
  let privateReads = 0;
  const partner = createEmbeddedPartner({ partnerClientType: type, issuer: 'https://identity.ankt.example.com/',
    productionHostOrigin: 'https://production.ankt.localhost:5593' }, {
    clientId: () => 'prepared-client', getClient: async () => { privateReads++; throw Error('No issuer in boundary unit test'); },
  });
  async function request(path, headers = {}, method = 'GET') {
    const output = { statusCode: 200, headers: {}, setHeader(key, value) { this.headers[key.toLowerCase()] = value; },
      writeHead(status, fields = {}) { this.statusCode = status; Object.entries(fields).forEach(([key, value]) => this.setHeader(key, value)); },
      end(body = '') { this.body = body; } };
    assert.equal(await partner.handle({ method, headers: { host: 'partner.ankt.example.com', ...headers } }, output, new URL(path, 'https://partner.ankt.example.com')), true);
    return output;
  }
  return { request, privateReads: () => privateReads };
}
test('browser configuration is public metadata only and never reads Confidential credentials', async () => {
  const f = fixture(), response = await f.request('/production/embedded-config');
  const value = JSON.parse(response.body);
  assert.deepEqual(Object.keys(value).sort(), ['clientId', 'clientType', 'hostOrigin', 'issuer', 'redirectUri']);
  assert.equal(f.privateReads(), 0); assert.equal(value.clientId, 'prepared-client');
});
test('completion without an initiating browser cookie cannot exchange or create a session', async () => {
  const f = fixture();
  for (const path of ['/production/embedded-callback?state=forged&code=untrusted', '/production/embedded-session']) {
    const response = await f.request(path); assert.ok([400, 401].includes(response.statusCode));
    assert.ok(!String(response.headers['set-cookie']).includes('__Host-ProductionPartner='));
  }
  assert.equal(f.privateReads(), 0);
});
test('Public completion only serves the SDK callback; the server never exchanges a Public code', async () => {
  const f = fixture('Public'), response = await f.request('/production/embedded-callback?code=untrusted&state=untrusted');
  assert.equal(response.statusCode, 200); assert.ok(response.body.includes('/production/embedded-callback.js'));
  assert.equal(f.privateReads(), 0); assert.ok(!response.body.includes('untrusted'));
});
test('invalid methods, host, scope, attempt and logout Origin fail before issuer access', async () => {
  const f = fixture();
  for (const [path, headers, method] of [
    ['/production/embedded-config', { host: 'wrong.example' }, 'GET'],
    ['/production/embedded-config', {}, 'POST'],
    ['/production/embedded-login?attempt=bad', {}, 'GET'],
    ['/production/embedded-login?attempt=11111111-1111-4111-8111-111111111111&scope=offline_access', {}, 'GET'],
    ['/production/embedded-logout', { origin: 'https://wrong.example' }, 'POST'],
  ]) assert.equal((await f.request(path, headers, method)).statusCode, 400);
  assert.equal(f.privateReads(), 0);
});
