import { spawn } from 'node:child_process';
import https from 'node:https';
import { readFile, realpath } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join, sep, basename } from 'node:path';
import { browserEnvironment } from '../SandboxExecutor/cdp.mjs';
import { closeOwnedBrowser } from './owned-browser-cleanup.mjs';
const root = resolve(import.meta.dirname, '../..');
export async function shutdown(directory) {
  const target = await realpath(directory);
  if (!target.startsWith(resolve(tmpdir()) + sep) || !/^ankt-oidc-browser-[a-f0-9]{32}$/.test(basename(target))) throw Error('Unexpected fixture directory');
  const config = JSON.parse(await readFile(join(target, 'fixture.json'), 'utf8'));
  if (!config.reviewFixture || config.issuer !== 'https://identity.ankt.example.com/') throw Error('Expected isolated review fixture');
  const ca = await readFile(join(target, 'root-ca.pem'));
  await new Promise((resolve, reject) => {
    const request = https.request('https://127.0.0.1:5490/fixture/executor-shutdown', { method: 'POST', ca, headers: { Authorization: 'SandboxExecutor ' + config.executorKey, Host: 'identity.ankt.example.com' } }, response => { response.resume(); response.once('end', resolve); });
    request.on('error', reject); request.setTimeout(5000, () => request.destroy()); request.end();
  });
}
if (process.argv[2] === '--stop') {
  // Called only for this run's internally created directory, never a live service.
  await shutdown(process.argv[3]);
} else {
  const production = process.argv.includes('--production-e2e');
  const host = spawn(join(root, '.codex-tmp/sdk8-check/sdk/dotnet.exe'), [join(root, 'tools/SsoE2EHost/bin/' + (production ? 'Release' : 'Debug') + '/net8.0/SsoE2EHost.dll'), '--review-e2e', ...(production ? ['--production-e2e'] : []), ...(process.argv.includes('--confidential-partner') ? ['--confidential-partner'] : []), ...(process.argv.includes('--non-sso-partner') ? ['--non-sso-partner'] : [])], { cwd: root, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: browserEnvironment() });
  host.stdin.on('error', error => { if (error.code !== 'EPIPE') { process.exitCode = 1; console.error('REVIEW_HOST_STOP_PIPE_FAILED'); } });
  let directory, child, buffer = '', failure = '', timedOut = false, hardStop;
  const browsers = new Map();
  const timer = setTimeout(() => stop(), 600000);
  const stop = () => {
    if (timedOut) return; timedOut = true;
    if (!host.stdin.destroyed && !host.stdin.writableEnded) host.stdin.end('stop\n');
    if (directory) void shutdown(directory).catch(() => {});
    if (child?.connected) child.send({ type: 'stop' });
    hardStop = setTimeout(() => {
      void Promise.allSettled([...browsers.values()].map(closeOwnedBrowser)).finally(() => { if (child?.exitCode === null) child.kill(); });
    }, 15000);
  };
  process.once('SIGINT', stop); process.once('SIGTERM', stop);
  try {
    directory = await new Promise((resolve, reject) => {
      host.once('error', reject); host.once('exit', () => reject(Error('Review host stopped before readiness: ' + failure.split(/\r?\n/)[0].slice(0, 180))));
      host.stderr.setEncoding('utf8'); host.stderr.on('data', chunk => { if (failure.length < 1000) failure += chunk; for (const line of chunk.split(/\r?\n/)) if (/^REVIEW_FIXTURE_FAILURE_[A-Za-z0-9_]+$/.test(line)) console.error(line); });
      host.stdout.setEncoding('utf8'); host.stdout.on('data', chunk => { buffer += chunk; const match = buffer.match(/Partner fixture directory: ([^\r\n]+)[\r\n]/); if (match) resolve(match[1]); if (buffer.length > 4096) buffer = buffer.slice(-4096); });
    });
    child = spawn(process.execPath, [join(import.meta.dirname, 'approved.e2e.mjs'), directory, ...(process.argv.includes('--all-flows') ? ['--all-flows'] : []), ...(process.argv.includes('--runtime-security') ? ['--runtime-security'] : []), ...(process.argv.includes('--stage10-baseline') ? ['--stage10-baseline'] : [])], { cwd: root, windowsHide: true, stdio: ['ignore', 'inherit', 'inherit', 'ipc'], env: { ...browserEnvironment(), ANKT_E2E_TRACK_BROWSERS: '1', NODE_EXTRA_CA_CERTS: join(directory, 'root-ca.pem') } });
    child.on('message', value => { if (value?.type === 'fixture-browser') browsers.set(value.pid, value); });
    const code = await new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
    if (code !== 0 || timedOut) process.exitCode = 1;
  } finally {
    clearTimeout(timer); clearTimeout(hardStop); process.removeListener('SIGINT', stop); process.removeListener('SIGTERM', stop);
    const cleanup = await Promise.allSettled([...browsers.values()].map(closeOwnedBrowser));
    for (const result of cleanup) if (result.status === 'rejected') {
      process.exitCode = 1;
      const reason = result.reason;
      console.error('REVIEW_BROWSER_CLEANUP_FAILED_' + (/^[A-Z0-9_]+$/.test(reason?.code ?? '') ? reason.code :
        reason?.message === 'Browser cleanup timed out' ? 'TIMEOUT' : reason?.message === 'Browser ownership cleanup failed' ? 'OWNERSHIP' : 'PROFILE'));
    }
    if (directory) await shutdown(directory).catch(() => {});
    if (!host.stdin.destroyed && !host.stdin.writableEnded) host.stdin.end('stop\n');
    if (host.exitCode === null) {
      let grace;
      await Promise.race([new Promise(resolve => host.once('exit', resolve)), new Promise(resolve => { grace = setTimeout(resolve, 50000); })]);
      clearTimeout(grace);
      if (host.exitCode === null) host.kill();
    }
  }
}
