import assert from 'node:assert/strict';
import { mkdir, writeFile } from 'node:fs/promises';
import { fixtureFetch } from '../SandboxExecutor/fixture-fetch.mjs';

export function baselineWorkloads(config, appId) {
  assert.equal(config.productionFixture, true, 'Owned Production fixture required');
  assert.equal(config.issuer, 'https://identity.ankt.example.com/', 'Only fixed loopback routing permitted');
  assert.match(appId, /^app_[a-z0-9]{43}$/);
  return [
    { name: 'catalog', path: '/api/mini-apps?page=1&pageSize=20', statuses: [200] },
    { name: 'detail', path: '/api/mini-apps/' + appId, statuses: [200] },
    { name: 'discovery', path: '/.well-known/openid-configuration', statuses: [200] },
    { name: 'jwks', path: '/.well-known/jwks.json', statuses: [200] },
    { name: 'authorize-denial', path: '/connect/authorize?client_id=stage10-invalid&response_type=code', statuses: [400, 429] },
    { name: 'token-denial', path: '/connect/token', method: 'POST', body: 'grant_type=invalid_stage10', statuses: [400, 401, 429] },
  ];
}

export async function boundedBaseline({ config, appId, fixture }) {
  const workloads = baselineWorkloads(config, appId);
  const before = await fixture('production-resources');
  const deadline = AbortSignal.timeout(20000);
  const started = performance.now(), results = [];
  for (const workload of workloads) {
    const timings = [], statuses = {}, phases = [];
    // 12 requests per shape, at most two in flight; no redirects or credential logging.
    for (const [phase, concurrency, count] of [['ramp-up', 1, 3], ['steady', 2, 6], ['ramp-down', 1, 3]]) {
      let next = 0;
      await Promise.all(Array.from({ length: concurrency }, async () => {
        while (next++ < count) {
          deadline.throwIfAborted();
          const start = performance.now();
          const response = await fixtureFetch(new URL(workload.path, config.issuer), {
            method: workload.method ?? 'GET', body: workload.body,
            headers: workload.body ? { 'Content-Type': 'application/x-www-form-urlencoded' } : {},
            redirect: 'error', signal: AbortSignal.any([deadline, AbortSignal.timeout(3000)]),
          });
          assert.ok(workload.statuses.includes(response.status), workload.name + ': unexpected status ' + response.status);
          timings.push(performance.now() - start); statuses[response.status] = (statuses[response.status] ?? 0) + 1;
        }
      }));
      phases.push({ phase, concurrency, count });
    }
    timings.sort((a, b) => a - b);
    const percentile = p => timings[Math.ceil(timings.length * p) - 1];
    results.push({ workload: workload.name, count: timings.length, phases, statuses,
      p50Ms: percentile(.5), p95Ms: percentile(.95), p99Ms: percentile(.99), maxMs: timings.at(-1) });
  }
  const after = await fixture('production-resources');
  const report = { results, elapsedMs: performance.now() - started, hostBefore: before, hostAfter: after,
    sqlSamples: await fixture('production-metrics'),
    limits: 'Local 72-request baseline only. Denial workloads do not measure successful OAuth throughput. Business launch timing is recorded separately by the real host E2E. No production SLO/capacity/RPO/RTO claim.' };
  const output = new URL('../../.codex-tmp/stage10/', import.meta.url);
  await mkdir(output, { recursive: true });
  await writeFile(new URL('baseline.json', output), JSON.stringify(report, null, 2));
  console.log('STAGE10_SIX_BOUNDED_WORKLOADS_72_REQUESTS_COMPLETED');
}
