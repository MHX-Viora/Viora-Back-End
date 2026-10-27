import assert from 'node:assert/strict';
import test from 'node:test';
import { spawn } from 'node:child_process';
import { stat } from 'node:fs/promises';
import { resolve, join } from 'node:path';
import { browserEnvironment } from '../SandboxExecutor/cdp.mjs';

test('cooperative stop closes the isolated HTTPS host and deletes its disposable fixture', async () => {
  const root = resolve(import.meta.dirname, '../..');
  const child = spawn(join(root, '.codex-tmp/sdk8-check/sdk/dotnet.exe'), [join(root, 'tools/SsoE2EHost/bin/Debug/net8.0/SsoE2EHost.dll'), '--review-e2e'], { cwd: root, windowsHide: true, stdio: ['pipe', 'pipe', 'pipe'], env: browserEnvironment() });
  child.stdin.on('error', error => { if (error.code !== 'EPIPE') throw error; });
  const exited = new Promise((resolve, reject) => { child.once('error', reject); child.once('exit', resolve); });
  let buffer = '', directory;
  let hardStop;
  const timer = setTimeout(() => { if (!child.stdin.destroyed && !child.stdin.writableEnded) child.stdin.end('stop\n'); hardStop = setTimeout(() => child.kill(), 15000); }, 60000);
  try {
    directory = await new Promise((resolve, reject) => {
      child.stdout.setEncoding('utf8'); child.stdout.on('data', chunk => { buffer += chunk; const match = buffer.match(/Partner fixture directory: ([^\r\n]+)[\r\n]/); if (match) resolve(match[1]); if (buffer.length > 4096) buffer = buffer.slice(-4096); });
      child.once('exit', () => reject(Error('Disposable host exited before readiness')));
    });
    child.stdin.end('stop\n'); assert.equal(await exited, 0);
    await assert.rejects(stat(directory), error => error.code === 'ENOENT');
  } finally {
    clearTimeout(timer); if (!child.stdin.destroyed && !child.stdin.writableEnded) child.stdin.end('stop\n');
    hardStop ??= setTimeout(() => child.kill(), 15000); await exited; clearTimeout(hardStop);
  }
});
