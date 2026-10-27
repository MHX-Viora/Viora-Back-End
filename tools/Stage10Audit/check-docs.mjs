import assert from 'node:assert/strict';
import { existsSync, readFileSync } from 'node:fs';
import { dirname, resolve } from 'node:path';
const root = resolve(import.meta.dirname, '../..');
const files = [
  'docs/specs/developer-platform-stage-10.md',
  'docs/plans/developer-platform-stage-10.md',
  'docs/handoffs/developer-platform-stage-10.md',
  'docs/releases/miniapp-platform-production-readiness.md',
  'docs/runbooks/miniapp-platform-deployment.md',
  ...['audit', 'architecture', 'security', 'database', 'performance', 'e2e'].map(name => 'docs/testing/developer-platform-stage-10-' + name + '.md'),
];
let links = 0;
for (const file of files) {
  const path = resolve(root, file), source = readFileSync(path, 'utf8');
  assert.ok(!/-----BEGIN (?:RSA |EC )?PRIVATE KEY-----/.test(source), 'Private key marker in ' + file);
  for (const match of source.replace(/```[\s\S]*?```/g, '').matchAll(/\[[^\]]+\]\(([^)]+)\)/g)) {
    const target = match[1].replace(/^<|>$/g, '').split('#')[0];
    if (!target || /^[a-z]+:/i.test(target)) continue;
    assert.ok(existsSync(resolve(dirname(path), decodeURIComponent(target))), 'Missing link from ' + file + ': ' + target);
    links++;
  }
}
const handoff = readFileSync(resolve(root, files[2]), 'utf8');
assert.equal([...handoff.matchAll(/^\| \d+ \|/gm)].length, 25, 'Expected 25 handoff topics');
const release = readFileSync(resolve(root, files[3]), 'utf8');
assert.equal([...release.matchAll(/^\| \d+ \|/gm)].length, 16, 'Expected 16 release groups');
const runbook = readFileSync(resolve(root, files[4]), 'utf8');
assert.equal([...runbook.matchAll(/^## \d+\./gm)].length, 11, 'Expected 11 runbook steps');
console.log(JSON.stringify({ documents: files.length, localLinks: links, handoffTopics: 25, releaseGroups: 16, runbookSteps: 11 }));
