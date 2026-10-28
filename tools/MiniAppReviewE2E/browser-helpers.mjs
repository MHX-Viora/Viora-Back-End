import assert from 'node:assert/strict';
import { setTimeout as delay } from 'node:timers/promises';
export const button = text => `[...document.querySelectorAll('button')].find(x=>x.textContent.trim()===${JSON.stringify(text)})`;
export async function page(cdp, url = 'about:blank', existingTarget) {
  const targetId = existingTarget ?? (await cdp.send('Target.createTarget', { url: 'about:blank' })).targetId;
  const { sessionId } = await cdp.send('Target.attachToTarget', { targetId, flatten: true });
  const call = (method, params) => cdp.send(method, params, sessionId);
  await call('Page.enable'); await call('Runtime.enable');
  const issues = [];
  const remove = cdp.on(event => {
    if (event.sessionId !== sessionId) return;
    if (event.method === 'Page.javascriptDialogOpening') void call('Page.handleJavaScriptDialog', { accept: true });
    if (event.method === 'Runtime.exceptionThrown' || event.method === 'Runtime.consoleAPICalled' && ['error', 'warning'].includes(event.params.type)) issues.push(event.method);
  });
  const read = async expression => {
    const result = await call('Runtime.evaluate', { expression, returnByValue: true });
    if (result.exceptionDetails) throw Error('DOM inspection unavailable'); return result.result?.value;
  };
  const wait = async (expression, label = expression.slice(0, 70)) => {
    const end = Date.now() + 40000;
    while (Date.now() < end) { try { if (await read(expression)) return; } catch {} await delay(100); }
    const diagnostic = await read(`({path:location.pathname,heading:document.querySelector('h1')?.textContent,alerts:[...document.querySelectorAll('[role=alert],[data-sonner-toast],.review-blockers')].map(x=>x.textContent.slice(0,180)),checks:[...document.querySelectorAll('.sandbox-results-table tbody tr')].filter(x=>x.children[1]?.textContent!=='Đạt').slice(0,18).map(x=>x.textContent.slice(0,180))})`);
    throw Error('Browser state timed out: ' + label + ' ' + JSON.stringify(diagnostic));
  };
  const focus = async expression => {
    if (!existingTarget) await cdp.send('Target.activateTarget', { targetId });
    await call('DOM.getDocument', { depth: 0 });
    const result = await call('Runtime.evaluate', { expression }); assert.ok(result.result?.objectId, 'Expected fixture control');
    const { nodeId } = await call('DOM.requestNode', { objectId: result.result.objectId });
    try { await call('DOM.scrollIntoViewIfNeeded', { nodeId }); await call('DOM.focus', { nodeId }); }
    catch { throw Error('Fixture control unavailable: ' + expression.slice(0, 160)); } return nodeId;
  };
  const key = async (key, modifiers = 0) => {
    const code = { Enter: 13, Escape: 27, Tab: 9, ArrowDown: 40, ArrowUp: 38, Home: 36, a: 65, ' ': 32 }[key];
    await call('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key === ' ' ? 'Space' : key, modifiers, windowsVirtualKeyCode: code, ...(key === 'Enter' ? { text: '\r' } : {}) });
    await call('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key === ' ' ? 'Space' : key, modifiers, windowsVirtualKeyCode: code });
  };
  const click = async expression => { await focus(expression); await key('Enter'); };
  const type = async (selector, text) => { await focus(`document.querySelector(${JSON.stringify(selector)})`); await key('a', 2); await call('Input.insertText', { text }); };
  const select = async (selector, index) => { await focus(`document.querySelector(${JSON.stringify(selector)})`); await key('Home'); for (let i = 0; i < index; i++) await key('ArrowDown'); await key('Enter'); };
  const navigate = async url => { await cdp.send('Target.activateTarget', { targetId }); await call('Page.navigate', { url }); };
  if (url) await navigate(url);
  return { cdp, call, read, wait, focus, key, click, type, select, navigate, issues, targetId, close: async () => { remove(); await cdp.send('Target.closeTarget', { targetId }); } };
}
