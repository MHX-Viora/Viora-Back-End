import test from 'node:test';
import assert from 'node:assert/strict';
import {randomUUID} from 'node:crypto';
import {createSandboxBridge} from '../../viora-BE/Developer/SandboxHost/bridge.mjs';

function fixture(validate=async()=>({appId:'app_test',environment:'Sandbox'}),close=async()=>{},report,openExternal) {
  const source={},sent=[];let validations=0,closed=0;
  const bridge=createSandboxBridge({origin:'https://partner.example.com',source:()=>source,appId:'app_test',
    validate:async()=>{validations++;return validate();},send:message=>sent.push(message),close:async()=>{closed++;await close();},report,openExternal});
  const event=(method,extra={})=>({origin:'https://partner.example.com',source,data:{channel:'ankt.miniapp',version:2,type:'request',id:randomUUID(),method},...extra});
  return {bridge,source,sent,event,get validations(){return validations;},get closed(){return closed;}};
}

test('host supports v1 and v2 handshakes but advertises only implemented Sandbox methods',async()=>{
  const f=fixture();const old=f.event('bridge.handshake');old.data.version=1;await f.bridge.handle(old);
  assert.deepEqual(f.sent.at(-1).result.permissions,['platform.getContext','platform.close']);
  await f.bridge.handle(f.event('bridge.handshake'));
  assert.deepEqual(f.sent.at(-1).result.permissions,['platform.getContext','platform.close','platform.getEnvironment','platform.isInsideAnkt']);
  await f.bridge.handle(f.event('platform.getContext'));
  assert.deepEqual(f.sent.at(-1).result,{appId:'app_test',environment:'Sandbox',runtime:'sandbox-web'});
  await f.bridge.handle(f.event('platform.getEnvironment'));assert.equal(f.sent.at(-1).result,'Sandbox');
  await f.bridge.handle(f.event('platform.isInsideAnkt'));assert.equal(f.sent.at(-1).result,true);
});

test('external links require v2 negotiated permission, exact params and current authorization',async()=>{
  const opened=[];const f=fixture(undefined,undefined,undefined,async url=>{opened.push(url);});
  await f.bridge.handle(f.event('bridge.handshake'));
  assert.ok(f.sent.at(-1).result.permissions.includes('platform.openExternalUrl'));
  const request=f.event('platform.openExternalUrl');request.data.params={url:'https://partner.example.com/help'};
  await f.bridge.handle(request);assert.deepEqual(opened,['https://partner.example.com/help']);assert.equal(f.sent.at(-1).result,null);
  const extra=f.event('platform.openExternalUrl');extra.data.params={url:'https://partner.example.com/help',confirmed:true};
  await f.bridge.handle(extra);assert.equal(f.sent.at(-1).error.code,'BRIDGE_PROTOCOL');assert.equal(opened.length,1);
  const old=f.event('platform.openExternalUrl');old.data.version=1;old.data.params=request.data.params;
  await f.bridge.handle(old);assert.equal(f.sent.at(-1).error.code,'UNSUPPORTED_METHOD');
});

test('external cancellation preserves bridge permissions and reset cancels pending confirmation',async()=>{
  let signal;const f=fixture(undefined,undefined,undefined,async(url,value)=>{signal=value;throw Object.assign(new Error(),{code:'BRIDGE_FORBIDDEN'});});
  await f.bridge.handle(f.event('bridge.handshake'));
  const request=f.event('platform.openExternalUrl');request.data.params={url:'https://partner.example.com/help'};
  await f.bridge.handle(request);assert.equal(f.sent.at(-1).error.code,'BRIDGE_FORBIDDEN');
  await f.bridge.handle(f.event('platform.getEnvironment'));assert.equal(f.sent.at(-1).result,'Sandbox');
  f.bridge.reset();assert.equal(signal.aborted,true);
});

test('close aborts external confirmation before server revocation completes',async()=>{
  let signal,finish;
  const f=fixture(undefined,()=>new Promise(resolve=>{finish=resolve;}),undefined,async(url,value)=>{signal=value;});
  await f.bridge.handle(f.event('bridge.handshake'));const request=f.event('platform.openExternalUrl');request.data.params={url:'https://partner.example.com/help'};
  await f.bridge.handle(request);const pending=f.bridge.handle(f.event('platform.close'));await new Promise(setImmediate);
  assert.equal(signal.aborted,true);finish();await pending;
});

test('concurrent close requests execute only once and deny further actions while closing',async()=>{
  let finish;const pending=new Promise(resolve=>{finish=resolve;});const f=fixture(undefined,()=>pending);
  await f.bridge.handle(f.event('bridge.handshake'));
  const first=f.bridge.handle(f.event('platform.close'));
  const second=f.bridge.handle(f.event('platform.close'));
  const context=f.bridge.handle(f.event('platform.getContext'));
  await new Promise(setImmediate);
  const calls=f.closed;finish();await Promise.all([first,second,context]);
  assert.equal(calls,1);assert.equal(f.closed,1);
  assert.ok(f.sent.filter(value=>!value.ok).every(value=>value.error.code==='BRIDGE_FORBIDDEN'));
});

test('missing own envelope keys cannot execute host actions',async()=>{
  const f=fixture();const event=f.event('bridge.handshake');
  event.data=Object.assign(Object.create({method:'bridge.handshake'}),event.data);delete event.data.method;
  await f.bridge.handle(event);assert.equal(f.validations,0);
});
test('spoofed origin/source/version and malformed request never reach a backend action',async()=>{
  const f=fixture();await f.bridge.handle(f.event('bridge.handshake',{origin:'https://evil.example'}));
  await f.bridge.handle(f.event('bridge.handshake',{source:{}}));const invalid=f.event('bridge.handshake');invalid.data.version=3;await f.bridge.handle(invalid);
  assert.equal(f.sent.length,0);assert.equal(f.validations,0);
  const credentials=f.event('bridge.handshake');credentials.data.access_token='never-forward';await f.bridge.handle(credentials);
  assert.equal(f.sent.at(-1).error.code,'BRIDGE_PROTOCOL');assert.equal(f.validations,0);
  await f.bridge.handle(f.event('platform.nativeCamera'));assert.equal(f.sent.at(-1).error.code,'UNSUPPORTED_METHOD');
  await f.bridge.handle(f.event('platform.getEnvironment'));assert.equal(f.sent.at(-1).error.code,'BRIDGE_FORBIDDEN');
});
test('request IDs cannot replay actions and close runs once',async()=>{
  const f=fixture();await f.bridge.handle(f.event('bridge.handshake'));const request=f.event('platform.getEnvironment');
  await f.bridge.handle(request);const count=f.validations;await f.bridge.handle(request);
  assert.equal(f.sent.at(-1).error.code,'BRIDGE_PROTOCOL');assert.equal(f.validations,count);
  const close=f.event('platform.close');await f.bridge.handle(close);await f.bridge.handle(close);assert.equal(f.closed,1);
});
test('navigation reset drops pending replies and requires a new handshake',async()=>{
  let finish;const pending=new Promise(resolve=>{finish=resolve;});const f=fixture(()=>pending);
  const handshake=f.bridge.handle(f.event('bridge.handshake'));f.bridge.reset();finish({appId:'app_test',environment:'Sandbox'});await handshake;
  assert.equal(f.sent.length,0);await f.bridge.handle(f.event('platform.getContext'));assert.equal(f.sent.at(-1).error.code,'BRIDGE_FORBIDDEN');
});

test('navigation cannot reset replay protection for a previously executed request ID',async()=>{
  const f=fixture();const request=f.event('bridge.handshake');await f.bridge.handle(request);
  f.bridge.reset();const count=f.validations;await f.bridge.handle(request);
  assert.equal(f.validations,count);assert.equal(f.sent.at(-1).error.code,'BRIDGE_PROTOCOL');
  await f.bridge.handle(f.event('bridge.handshake'));assert.equal(f.sent.at(-1).ok,true);
});
test('host caps pending requests and sanitizes backend authorization errors',async()=>{
  let finish;const pending=new Promise(resolve=>{finish=resolve;});const f=fixture(()=>pending);
  const calls=Array.from({length:8},()=>f.bridge.handle(f.event('bridge.handshake')));
  await f.bridge.handle(f.event('bridge.handshake'));assert.equal(f.sent.at(-1).error.code,'BRIDGE_BUSY');assert.equal(f.validations,8);
  finish({appId:'app_test',environment:'Sandbox'});await Promise.all(calls);
  const g=fixture(async()=>{throw new Error('private token material');});await g.bridge.handle(g.event('bridge.handshake'));
  assert.equal(g.sent.at(-1).error.code,'BRIDGE_FORBIDDEN');assert.ok(!JSON.stringify(g.sent).includes('private'));
});

test('v2 diagnostics capability forwards only catalog observations after permission and schema validation',async()=>{
  const reports=[];const f=fixture(undefined,undefined,async value=>{reports.push(value);});
  await f.bridge.handle(f.event('bridge.handshake'));assert.ok(f.sent.at(-1).result.permissions.includes('diagnostics.report'));
  const event=f.event('diagnostics.report');event.data.params={category:'Bridge',code:'BRIDGE_READY',count:2};
  await f.bridge.handle(event);assert.deepEqual(reports,[event.data.params]);assert.equal(f.sent.at(-1).result,null);
  for(const params of [{category:'Bridge',code:'BRIDGE_READY',token:'never-send'},{category:'Runtime',code:'SSO_SUCCEEDED'},{category:'Bridge',code:'BRIDGE_READY',count:1000001}]){
    const bad=f.event('diagnostics.report');bad.data.params=params;await f.bridge.handle(bad);assert.equal(f.sent.at(-1).error.code,'BRIDGE_PROTOCOL');
  }
  assert.equal(reports.length,1);
  const old=f.event('bridge.handshake');old.data.version=1;await f.bridge.handle(old);assert.ok(!f.sent.at(-1).result.permissions.includes('diagnostics.report'));
});

test('diagnostic throttling reports busy without disabling platform controls',async()=>{
  const f=fixture(undefined,undefined,async()=>{throw Object.assign(new Error(),{code:'BRIDGE_BUSY'});});
  await f.bridge.handle(f.event('bridge.handshake'));const event=f.event('diagnostics.report');event.data.params={category:'Bridge',code:'BRIDGE_READY'};
  await f.bridge.handle(event);assert.equal(f.sent.at(-1).error.code,'BRIDGE_BUSY');
  await f.bridge.handle(f.event('platform.getEnvironment'));assert.equal(f.sent.at(-1).result,'Sandbox');
});
