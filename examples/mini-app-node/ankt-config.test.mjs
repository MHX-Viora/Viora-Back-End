import { test } from 'node:test';
import assert from 'node:assert/strict';
import { loadAnktConfig } from './ankt-config.mjs';

const basic = { ANKT_CLIENT_ID: 'test-client', ANKT_CLIENT_SECRET: 'test-secret', ANKT_REDIRECT_URI: 'https://nghethuatso.vn/auth/ankt/callback' };
test('three variables select official endpoints and secure website settings', () => {
  const config = loadAnktConfig(basic);
  assert.equal(config.anktApi.origin, 'https://api.mxh.ankt.vn');
  assert.equal(config.anktHost, 'https://app.ankt.vn');
  assert.equal(config.origin.origin, 'https://nghethuatso.vn');
  assert.equal(config.callback, basic.ANKT_REDIRECT_URI);
  assert.equal(config.production, true);
});
test('custom callback path is retained exactly', () => {
  assert.equal(loadAnktConfig({...basic, ANKT_REDIRECT_URI: 'https://nghethuatso.vn/social/ankt'}).callbackPath, '/social/ankt');
});
test('unsafe callback and endpoints fail without echoing values', () => {
  for (const uri of ['http://nghethuatso.vn/callback', 'https://user:private@nghethuatso.vn/callback', 'https://nghethuatso.vn/callback#fragment', 'https://nghethuatso.vn/callback?fixed=1', 'https://nghethuatso.vn/login']) {
    assert.throws(() => loadAnktConfig({...basic, ANKT_REDIRECT_URI: uri}), error => !error.message.includes(uri));
  }
  assert.throws(() => loadAnktConfig({...basic, ANKT_API_URL: 'http://localhost:5000'}));
  assert.throws(() => loadAnktConfig({NODE_ENV:'production', ANKT_CLIENT_ID:'test'}));
});
test('existing localhost development overrides remain supported', () => {
  const config = loadAnktConfig({NODE_ENV:'development', MINI_APP_ORIGIN:'http://127.0.0.1:5310', ANKT_API_URL:'http://127.0.0.1:5000', ANKT_APP_ORIGIN:'http://127.0.0.1:8081', ANKT_CLIENT_ID:'test'});
  assert.equal(config.callback, 'http://127.0.0.1:5310/auth/ankt/callback');
  assert.equal(config.production, false);
});
