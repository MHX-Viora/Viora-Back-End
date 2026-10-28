import assert from 'node:assert/strict';
import { writeFile } from 'node:fs/promises';
import { join } from 'node:path';
export async function lifecycleSample(parent, phase) {
  await parent.call('Performance.enable');
  const dom = await parent.call('Memory.getDOMCounters');
  const data = (await parent.call('Performance.getMetrics')).metrics;
  const heapBytes = data.find(metric => metric.name === 'JSHeapUsedSize')?.value;
  assert.ok(Number.isFinite(heapBytes) && heapBytes > 0);
  assert.equal(await parent.read('document.querySelectorAll("iframe").length'), 1);
  return { phase, heapBytes, documents: dom.documents, nodes: dom.nodes, listeners: dom.jsEventListeners };
}
export async function recordProductionPerformance({ fixture, config, samples, initialLaunchMs, logoRequests }) {
  const requests = await fixture('production-metrics');
  const catalog = requests.filter(sample => sample.category === 'catalog' && sample.status === 200);
  assert.ok(catalog.length > 0);
  assert.ok(catalog.every(sample => sample.queries === 2), 'Catalog count/page use two SQL reads, independent of per-item HTTP');
  const result = { clientType: config.partnerClientType, initialLaunchMs, logoRequests, requests, lifecycle: samples,
    limits: 'Three lifecycle samples without forced GC; observations only, not a memory-leak or latency benchmark pass. Native not measured.' };
  await writeFile(join(import.meta.dirname, '../../.codex-tmp/stage8-performance-' + config.partnerClientType.toLowerCase() + '.json'), JSON.stringify(result, null, 2));
  console.log('STAGE8_REAL_CATALOG_TWO_QUERIES_LAUNCH_AND_THREE_LIFECYCLE_SAMPLES_' + config.partnerClientType.toUpperCase());
}
