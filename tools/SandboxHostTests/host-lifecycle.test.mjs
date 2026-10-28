import test from 'node:test';
import assert from 'node:assert/strict';
import {readFile} from 'node:fs/promises';
import vm from 'node:vm';
import {randomUUID} from 'node:crypto';
import {createSandboxBridge} from '../../viora-BE/Developer/SandboxHost/bridge.mjs';
import {createExternalLinkOpener} from '../../viora-BE/Developer/SandboxHost/external-link.mjs';

const script=(await readFile(new URL('../../viora-BE/Developer/SandboxHost/host.mjs',import.meta.url),'utf8')).replace(/^import .*;\r?\n/gm,'');
async function fixture({deferContext=false,failClose=false}={}) {
  const elements=new Map(),listeners=new Map(),sent=[],requests=[];
  const node=()=>({disabled:true,textContent:'',listeners:new Map(),addEventListener(type,fn){this.listeners.set(type,fn);},setAttribute(){},remove(){this.removed=true;},append(value){this.child=value;}});
  for(const key of ['#status','#preview','#back','#close','#app-name'])elements.set(key,node());
  const frame=node();frame.contentWindow={postMessage(){}};
  let finishClose,finishContext;
  const parent={postMessage:value=>sent.push(value)};
  const window={parent,addEventListener:(type,fn)=>listeners.set(type,fn),removeEventListener:(type,fn)=>{if(listeners.get(type)===fn)listeners.delete(type);}};
  const context={sessionId:'11111111-1111-4111-8111-111111111111',appId:'app_test',name:'Partner',environment:'Sandbox',partnerOrigin:'https://partner.example.com',websiteUrl:'https://partner.example.com/app',ssoEnabled:false};
  const timers=new Set();
  vm.runInNewContext(script,{createSandboxBridge,createExternalLinkOpener,URL,AbortController,AbortSignal,window,
    document:{querySelector:key=>elements.get(key),createElement:()=>frame},
    location:{pathname:'/sandbox-host/'+context.sessionId+'/run'},
    setTimeout:fn=>{timers.add(fn);return fn;},clearTimeout:fn=>timers.delete(fn),setInterval:fn=>fn,clearInterval(){},
    fetch:async(path)=>{requests.push(path);if(path==='/sandbox-runtime/close')return new Promise(resolve=>{finishClose=()=>resolve({ok:!failClose,status:failClose?503:204});});
      if(path==='/sandbox-runtime/context'&&deferContext)return new Promise(resolve=>{finishContext=()=>resolve({ok:true,status:200,json:async()=>context});});
      return {ok:true,status:200,json:async()=>path==='/sandbox-host/config'?{portalOrigin:'https://portal.example.com'}:context};},
  });
  await new Promise(setImmediate);
  return {elements,frame,listeners,sent,requests,timers,finishClose:()=>finishClose(),finishContext:()=>finishContext()};
}
test('host stops partner execution immediately while revocation is pending and coalesces repeated close',async()=>{
  const f=await fixture();assert.equal(f.elements.get('#preview').child,f.frame);
  const click=f.elements.get('#close').listeners.get('click');click();click();
  assert.equal(f.frame.removed,true);
  assert.equal(f.requests.filter(path=>path==='/sandbox-runtime/close').length,1);
  f.finishClose();await new Promise(setImmediate);
  assert.equal(f.sent.at(-1).type,'host.closed');
});
test('every observed partner navigation starts a fresh bounded handshake wait',async()=>{
  const f=await fixture();const load=f.frame.listeners.get('load');load();
  // Simulate the initial connection clearing its timer before a later navigation.
  f.timers.clear();load();assert.equal(f.timers.size,1);
  assert.equal(f.sent.at(-1).type,'bridge.disconnected');
});

test('closing during runtime authorization never restores a late partner frame',async()=>{
  const f=await fixture({deferContext:true});f.elements.get('#close').listeners.get('click')();
  f.finishContext();await new Promise(setImmediate);
  assert.equal(f.elements.get('#preview').child,undefined);
  f.finishClose();await new Promise(setImmediate);
  assert.equal(f.sent.at(-1).type,'host.closed');
});

test('failed revocation retains actionable failure and permits an explicit retry',async()=>{
  const f=await fixture({failClose:true});const click=f.elements.get('#close').listeners.get('click');
  click();f.finishClose();await new Promise(setImmediate);
  assert.equal(f.frame.removed,true);assert.equal(f.sent.at(-1).type,'host.close_failed');
  click();assert.equal(f.requests.filter(path=>path==='/sandbox-runtime/close').length,2);
  f.finishClose();await new Promise(setImmediate);
});

test('host reports its own bridge observation separately from SDK reports',async()=>{
  const f=await fixture();const receive=f.listeners.get('message');
  receive({origin:'https://partner.example.com',source:f.frame.contentWindow,data:{channel:'ankt.miniapp',version:2,type:'request',id:randomUUID(),method:'bridge.handshake'}});
  await new Promise(setImmediate);
  assert.equal(f.requests.filter(path=>path==='/sandbox-runtime/host-observations').length,1);
});
