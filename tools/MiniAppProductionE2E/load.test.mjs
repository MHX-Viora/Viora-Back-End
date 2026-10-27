import test from 'node:test';
import assert from 'node:assert/strict';
import { baselineWorkloads } from './load.e2e.mjs';

test('load targets require the owned issuer and cannot address arbitrary production URLs', () => {
  const config = { productionFixture: true, issuer: 'https://identity.ankt.example.com/' };
  const appId = 'app_' + 'e'.repeat(43);
  assert.throws(() => baselineWorkloads({ ...config, issuer: 'https://mxh.ankt.vn/' }, appId));
  assert.throws(() => baselineWorkloads({ ...config, productionFixture: false }, appId));
  assert.throws(() => baselineWorkloads(config, '../external'));
  const shapes = baselineWorkloads(config, appId);
  assert.equal(shapes.length, 6);
  assert.ok(shapes.every(shape => new URL(shape.path, config.issuer).origin === new URL(config.issuer).origin));
});
