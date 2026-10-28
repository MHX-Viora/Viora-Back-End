// UI fixture only. Requires ../viora/features/mini-apps/tests/serve-web-ui.cjs.
// Real components, simulated catalog/host; no publication, launch or SSO acceptance.
import assert from 'node:assert/strict';
import { spawn } from 'node:child_process';
import { mkdtemp, readFile, writeFile, rm, realpath } from 'node:fs/promises';
import { tmpdir } from 'node:os';
import { resolve, join, dirname } from 'node:path';
import { once } from 'node:events';
const pause = ms => new Promise(resolve => setTimeout(resolve, ms));
const origin = 'http://127.0.0.1:5498';
const profile = await mkdtemp(join(tmpdir(), 'ankt-stage8-ui-'));
const chrome = process.env.STAGE8_CHROME || 'C:/Program Files/Google/Chrome/Application/chrome.exe';
const browser = spawn(chrome, ['--headless=new', '--disable-gpu', '--no-first-run', '--no-default-browser-check',
  '--disable-background-networking', '--disable-extensions', '--disable-component-update', '--remote-debugging-address=127.0.0.1',
  '--remote-debugging-port=0', '--user-data-dir=' + profile, 'about:blank'], { windowsHide: true, stdio: 'ignore' });
const output = resolve(import.meta.dirname, '../../.codex-tmp');
const pending = new Map(), issues = [], requests = new Set();
let socket, sequence = 0, phase = 'startup';
browser.on('error', error => issues.push(error.message));
function rpc(method, params = {}) {
  return new Promise((resolve, reject) => {
    const id = ++sequence, timeout = setTimeout(() => { pending.delete(id); reject(new Error('CDP timeout: ' + method)); }, 12000);
    pending.set(id, { resolve: result => { clearTimeout(timeout); resolve(result); }, reject: error => { clearTimeout(timeout); reject(error); } });
    socket.send(JSON.stringify({ id, method, params }));
  });
}
async function read(expression) {
  const result = await rpc('Runtime.evaluate', { expression, returnByValue: true });
  assert.ok(!result.exceptionDetails, 'Inspection failed'); return result.result?.value;
}
async function wait(expression, label) {
  const until = Date.now() + 12000;
  while (Date.now() < until) { if (await read(expression)) return; await pause(40); }
  throw new Error('UI timeout: ' + label + '; ' + await read('JSON.stringify({focus:document.activeElement?.textContent?.slice(0,90),route:window.__miniAppUi?.route,details:window.__miniAppUi?.detailCalls,mounts:window.__miniAppUi?.mounts})'));
}
const control = label => `[...document.querySelectorAll('button,[role=button]')].find(e=>(e.getAttribute('aria-label')||e.textContent.trim())===${JSON.stringify(label)})`;
async function click(label) {
  const expression = control(label), node = await rpc('Runtime.evaluate', { expression });
  assert.ok(node.result?.objectId, 'Missing control: ' + label);
  await rpc('DOM.scrollIntoViewIfNeeded', { objectId: node.result.objectId });
  const box = await read(`(()=>{const r=${expression}.getBoundingClientRect();return {x:r.x+r.width/2,y:r.y+r.height/2}})()`);
  await rpc('Input.dispatchMouseEvent', { type: 'mousePressed', ...box, button: 'left', clickCount: 1 });
  await rpc('Input.dispatchMouseEvent', { type: 'mouseReleased', ...box, button: 'left', clickCount: 1 });
}
async function key(key) {
  const keyCode = { Enter: 13, Tab: 9, Escape: 27 }[key];
  await rpc('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key, windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode,
    ...(key === 'Enter' ? { text: '\r', unmodifiedText: '\r' } : {}) });
  await rpc('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key, windowsVirtualKeyCode: keyCode, nativeVirtualKeyCode: keyCode });
}
async function dimensions(width, host = false) {
  await rpc('Emulation.setDeviceMetricsOverride', { width, height: 900, deviceScaleFactor: 1, mobile: false });
  await rpc('Emulation.setPageScaleFactor', { pageScaleFactor: 1 });
  await wait(`innerWidth===${width}`, 'actual viewport ' + width);
  await pause(120);
  assert.equal(await read('document.documentElement.scrollWidth<=innerWidth+1'), true, 'Overflow at ' + width);
  const controls = await read(`[...document.querySelectorAll('[role=button]')].map(e=>({label:e.getAttribute('aria-label')||e.textContent.trim(),height:e.getBoundingClientRect().height,width:e.getBoundingClientRect().width}))`);
  assert.ok(controls.length > 0);
  assert.ok(controls.every(control => { const minimum=['Quay về ANKT','Hỗ trợ Mini App'].includes(control.label)?36:44; return control.height>=minimum&&control.width>=minimum; }), 'Touch controls at ' + width);
  if(host)assert.equal(await read(`document.querySelector('[data-testid="mini-app-toolbar"]').getBoundingClientRect().height`),40,'Compact toolbar height');
  if ([320, 1440].includes(width)) {
    const shot = await rpc('Page.captureScreenshot', { format: 'png' });
    await writeFile(join(output, `stage8-ui-${host ? 'host' : 'catalog'}-${width}.png`), Buffer.from(shot.data, 'base64'));
  }
}
async function main() {
  let target;
  for (let attempt = 0; attempt < 60; attempt++) {
    try {
      const port = Number((await readFile(join(profile, 'DevToolsActivePort'), 'utf8')).split(/\r?\n/)[0]);
      assert.ok(Number.isInteger(port) && port > 0 && port <= 65535);
      target = (await (await fetch(`http://127.0.0.1:${port}/json`)).json()).find(page => page.type === 'page');
      if (target) break;
    } catch {} await pause(100);
  }
  assert.ok(target, 'Owned Chrome did not start');
  socket = new WebSocket(target.webSocketDebuggerUrl); await once(socket, 'open');
  socket.addEventListener('message', event => {
    const value = JSON.parse(event.data), callback = pending.get(value.id);
    if (callback) { pending.delete(value.id); value.error ? callback.reject(new Error(value.error.message)) : callback.resolve(value.result); }
    if (value.method === 'Runtime.exceptionThrown' || value.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(value.params.type)) issues.push(value.method);
    if (value.method === 'Network.requestWillBeSent') requests.add(value.params.request.url);
  });
  await rpc('Page.enable'); await rpc('Runtime.enable'); await rpc('DOM.enable'); await rpc('Network.enable');
  phase = 'catalog'; await rpc('Page.navigate', { url: origin });
  await wait(`document.querySelectorAll('[aria-label^="Mở "]').length===20`, '20 catalog items');
  assert.equal(await read('document.querySelectorAll("script").length'), 1, 'Partner name remains text');
  assert.equal(await read(`!!document.querySelector('[aria-label="Wallet sentinel"]')`), true);
  for (const width of [320, 375, 768, 1440]) await dimensions(width);
  await click('Xem thêm'); await wait(`document.querySelectorAll('[aria-label^="Mở "]').length===24`, 'pagination');
  await click('error'); await wait('document.body.textContent.includes("Không thể tải Mini App")', 'catalog failure');
  assert.equal(await read(`!!document.querySelector('[aria-label="Wallet sentinel"]')`), true);
  await click('Thử lại'); await wait('window.__miniAppUi.listCalls>=4&&!document.querySelector("[aria-busy=true]")', 'retry without loop');
  await click('empty'); await wait('document.body.textContent.includes("Chưa có Mini App khả dụng")', 'empty');
  await click('ready'); await wait(`document.querySelectorAll('[aria-label^="Mở "]').length===20`, 'online refresh');
  phase = 'keyboard open';
  const node = await rpc('Runtime.evaluate', { expression: control('Mở <script>alert(1)</script>') });
  await rpc('DOM.focus', { objectId: node.result.objectId }); await key('Enter');
  await wait(`!!document.querySelector('[aria-label="Simulated host"]')`, 'host mounted');
  assert.equal(await read('document.activeElement.getAttribute("aria-label")'), 'Quay về ANKT', 'Focus moves to host back arrow');
  for (const width of [320, 375, 768, 1440]) await dimensions(width, true);
  await click('Fixture: SDK connected'); await click('Fixture: website loaded');
  assert.equal(await read('document.body.textContent.includes("Đã kết nối SDK · Production")'), true);
  phase = 'support'; await click('Hỗ trợ Mini App');
  await wait(`window.__miniAppUi.lastNavigation==='/support'&&!!document.querySelector('main')`, 'support navigation');
  await wait(`!!${control('Mở <script>alert(1)</script>')}`, 'catalog after support');
  await click('Mở <script>alert(1)</script>');
  await wait('window.__miniAppUi.mounts===2&&window.__miniAppUi.disposed===1', 'reopen disposal');
  await click('Fixture: session revoked'); await wait('window.__miniAppUi.disposed===2', 'revocation disposal');
  await click('Thử lại'); await wait('window.__miniAppUi.mounts===3', 'fresh retry');
  await click('Fixture: confirmed close'); await wait('window.__miniAppUi.disposed===3', 'confirmed close');
  await click('Thử lại'); await wait('window.__miniAppUi.mounts===4', 'reopen after close');
  await click('Quay về ANKT'); await wait('!!document.querySelector("main")&&window.__miniAppUi.disposed===4', 'return to catalog');
  assert.equal(issues.length, 0, 'Console issues: ' + issues.join(', '));
  assert.ok([...requests].every(url => url.startsWith(origin + '/') || url.startsWith('data:')), 'Fixture sent external request');
  console.log('PASS UI-only: exact 320/375/768/1440 viewports, 40px toolbar with 36px icons, no overflow, escaped names, pagination/error/empty/retry, keyboard focus, back/support navigation, SDK status ordering and close/revoke disposal. Zero console issues/external requests. Services simulated; no business/native acceptance.');
}
try { await main(); } catch (error) { console.error(phase + ': ' + error.message); process.exitCode = 1; }
finally {
  if (socket?.readyState === WebSocket.OPEN) { await rpc('Browser.close').catch(() => {}); socket.close(); }
  if (browser.exitCode === null) { await Promise.race([once(browser, 'exit'), pause(2000)]); if (browser.exitCode === null) { browser.kill(); await Promise.race([once(browser, 'exit'), pause(2000)]); } }
  // Resolve and verify the newly created owned profile before recursive cleanup.
  const actual = await realpath(profile), parent = await realpath(tmpdir());
  assert.equal(dirname(actual).toLowerCase(), parent.toLowerCase());
  assert.ok(actual.split(/[\\/]/).at(-1).startsWith('ankt-stage8-ui-'));
  await rm(actual, { recursive: true, force: true, maxRetries: 5, retryDelay: 200 });
}
