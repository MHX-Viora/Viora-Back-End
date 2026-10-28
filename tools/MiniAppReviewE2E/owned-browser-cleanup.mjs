import { spawn } from 'node:child_process';
import { lstat, realpath, rm } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join, sep, basename, dirname } from 'node:path';
async function profileExists(target) {
  try {
    const info = await lstat(target);
    if (!info.isDirectory() || info.isSymbolicLink()) throw Error('Unexpected browser profile');
    const actual = await realpath(target), allowed = await realpath(tmpdir());
    if (dirname(actual).toLowerCase() !== allowed.toLowerCase() || !basename(actual).startsWith('ankt-sandbox-executor-'))
      throw Error('Unexpected browser profile');
    return true;
  } catch (error) { if (error.code === 'ENOENT') return false; throw error; }
}
export async function closeOwnedBrowser(value) {
  if (!Number.isSafeInteger(value.pid) || value.pid < 1 || typeof value.profile !== 'string' || typeof value.websocket !== 'string' || !/^ws:\/\/127\.0\.0\.1:\d{1,5}\/devtools\/browser\/[a-z0-9-]+$/.test(value.websocket)) return;
  const target = resolve(value.profile);
  if (!target.startsWith(resolve(tmpdir()) + sep) || !basename(target).startsWith('ankt-sandbox-executor-')) return;
  // The child normally closes Chrome and removes this profile first. A stale
  // receipt must not inspect or stop a PID that the OS may already have reused.
  if (!await profileExists(target)) return;
  await new Promise(resolve => {
    const socket = new WebSocket(value.websocket), timeout = setTimeout(() => { socket.close(); resolve(); }, 2000);
    socket.addEventListener('open', () => socket.send(JSON.stringify({ id: 1, method: 'Browser.close' })), { once: true });
    socket.addEventListener('error', () => { clearTimeout(timeout); resolve(); }, { once: true });
    socket.addEventListener('close', () => { clearTimeout(timeout); resolve(); }, { once: true });
  });
  if (!await profileExists(target)) return;
  try { await new Promise((resolve, reject) => {
    const helper = spawn(join(process.env.SystemRoot ?? 'C:/Windows', 'System32/WindowsPowerShell/v1.0/powershell.exe'), ['-NoProfile', '-NonInteractive', '-File', join(import.meta.dirname, 'stop-browser.ps1'), '-BrowserPid', String(value.pid), '-ProfilePath', target], { windowsHide: true, stdio: 'ignore' });
    const timer = setTimeout(() => { helper.kill(); reject(Error('Browser cleanup timed out')); }, 10000);
    helper.once('error', error => { clearTimeout(timer); reject(error); });
    helper.once('exit', code => { clearTimeout(timer); code === 0 ? resolve() : reject(Error('Browser ownership cleanup failed')); });
  }); } catch (error) { if (await profileExists(target)) throw error; return; }
  // Only GUID profiles explicitly reported by this task's child are eligible.
  if (await profileExists(target)) await rm(target, { recursive: true, force: true, maxRetries: 5, retryDelay: 100 });
}
