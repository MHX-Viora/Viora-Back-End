import { test } from 'node:test';
import assert from 'node:assert/strict';
import { readFileSync } from 'node:fs';
import { runInNewContext } from 'node:vm';

const source = readFileSync(new URL('../viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js', import.meta.url), 'utf8');
function fixture(native = false) {
  const handlers = new Map(); const sent = []; const timers = new Map(); let sequence = 0;
  const parent = {postMessage: (value, origin) => sent.push({value,origin})};
  const window = {parent, addEventListener: (type, handler) => handlers.set(type,handler), ...(native ? {ReactNativeWebView:{postMessage: value => sent.push({value:JSON.parse(value)})}} : {})};
  runInNewContext(source, {window, URL, Map, Set, Date, Promise, Object, Error, JSON, setTimeout: callback => {timers.set(++sequence,callback);return sequence;}, clearTimeout: id => timers.delete(id)});
  const context = {type:'ANKT_MINI_APP_READY',appId:'app-1',sessionId:'session-1',nonce:'fresh-nonce'};
  const message = (data, origin = 'https://ankt.example', eventSource = parent) => handlers.get('message')({data,origin,source:eventSource});
  const ready = () => {if(native) window.__ANKT_NATIVE_READY__(context); else {window.ANKT.configure({hostOrigin:'https://ankt.example'});message(context);}};
  return {window,parent,sent,timers,handlers,context,message,ready};
}
const flush = () => new Promise(resolve => setImmediate(resolve));
test('web SDK ignores forged parent/source/origin handshakes', async () => {
  const f = fixture(); f.window.ANKT.configure({hostOrigin:'https://ankt.example'});
  f.message(f.context,'https://attacker.example'); f.message(f.context,'https://ankt.example',{});
  const request = f.window.ANKT.getAppInfo(); await flush(); assert.equal(f.sent.length,0);
  f.message(f.context); await flush(); assert.equal(f.sent.length,1);
  const id = f.sent[0].value.id;
  f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id,result:{name:'Verified app'}});
  assert.equal((await request).name,'Verified app'); assert.equal(f.sent[0].origin,'https://ankt.example');
});
test('response must match app/session/nonce and real source', async () => {
  const f=fixture();f.ready();const request=f.window.ANKT.getGrantedPermissions();await flush();let resolved=false;request.then(()=>{resolved=true;});const id=f.sent[0].value.id;
  f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',nonce:'wrong',id,result:['profile.email']});
  f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id,result:['profile.email']},'https://ankt.example',{});await flush();assert.equal(resolved,false);
  f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id,result:['profile.basic']});assert.deepEqual(Array.from(await request),['profile.basic']);
});
test('native SDK cannot establish binding from arbitrary window messages', async () => {
  const f=fixture(true);f.message(f.context,'',null);const request=f.window.ANKT.getPlatformInfo();await flush();assert.equal(f.sent.length,0);f.ready();await flush();assert.equal(f.sent.length,1);
  f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id:f.sent[0].value.id,result:{platform:'android'}},'',null);assert.equal((await request).platform,'android');
});
test('SDK validates methods/payloads and propagates denied permissions', async () => {
  const f=fixture();f.ready();await assert.rejects(f.window.ANKT.requestLogin({state:'short'}),{code:'INVALID_REQUEST'});await assert.rejects(f.window.ANKT.requestPermission('invalid scope'),{code:'INVALID_REQUEST'});
  const request=f.window.ANKT.requestPermission({permission:'profile.basic'});await flush();f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id:f.sent[0].value.id,error:{code:'CONSENT_DENIED',message:'Permission denied'}});await assert.rejects(request,{code:'CONSENT_DENIED'});
});
test('navigation rejects pending requests and stale responses', async () => {
  const f=fixture();f.ready();const request=f.window.ANKT.getAppInfo();await flush();const rejected=assert.rejects(request,{code:'SESSION_CLOSED'});f.handlers.get('pagehide')();await rejected;assert.equal(f.timers.size,0);
});
test('SDK limits request rate and pending calls time out', async () => {
  const f=fixture();f.ready();for(let i=0;i<30;i++){const request=f.window.ANKT.getAppInfo();await flush();f.message({...f.context,type:'ANKT_MINI_APP_RESPONSE',id:f.sent[i].value.id,result:{}});await request;}await assert.rejects(f.window.ANKT.getAppInfo(),{code:'RATE_LIMITED'});
  const timeout=fixture();timeout.ready();const request=timeout.window.ANKT.getAppInfo();const rejected=assert.rejects(request,{code:'BRIDGE_TIMEOUT'});await flush();for(const callback of timeout.timers.values())callback();await rejected;
});
