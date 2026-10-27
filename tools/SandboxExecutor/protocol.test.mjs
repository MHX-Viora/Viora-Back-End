import test from 'node:test';
import assert from 'node:assert/strict';
import {validateJob,validateSignal,hasPartnerFrame,mergeFrameTrees} from './protocol.mjs';
import {browserEnvironment} from './cdp.mjs';
test('untrusted browser process cannot inherit executor or application credentials',()=>{
  assert.deepEqual(browserEnvironment({SystemRoot:'C:/Windows',TEMP:'temporary',ANKT_SANDBOX_EXECUTOR_KEY:'secret',DATABASE_URL:'secret',HTTP_PROXY:'secret'}),{SystemRoot:'C:/Windows',TEMP:'temporary'});
});
test('cross-process frame snapshots merge only through actual CDP frame IDs',()=>{
  const root={frame:{id:'portal',url:'https://portal.example'},childFrames:[{frame:{id:'host',parentId:'portal',url:'https://host.example'}}]};
  const child={frame:{id:'host',parentId:'portal',url:'https://host.example'},childFrames:[{frame:{id:'partner',parentId:'host',url:'https://partner.example'}}]};
  assert.equal(mergeFrameTrees(root,[child]).childFrames[0].childFrames[0].frame.id,'partner');
  assert.equal(mergeFrameTrees(root,[{frame:{id:'other',url:'https://partner.example'}}]).childFrames[0].childFrames.length,0);
});
const job=()=>({runId:'11111111-1111-4111-8111-111111111111',sessionId:'22222222-2222-4222-8222-222222222222',appId:'app_fixture',configurationFingerprint:'A'.repeat(64),
  websiteUrl:'https://partner.example.com/',partnerOrigin:'https://partner.example.com',hostOrigin:'https://sandbox.ankt.example',portalOrigin:'https://portal.ankt.example',deadlineAt:new Date(Date.now()+120000).toISOString(),
  checks:[{key:'runtime',challenge:'opaque-protected-proof',expiresAt:new Date(Date.now()+60000).toISOString()}]});
test('executor accepts bounded live server job only with distinct exact HTTPS origins',()=>{
  assert.equal(validateJob(job()).appId,'app_fixture');
  for(const change of [{hostOrigin:'https://portal.ankt.example'},{websiteUrl:'https://evil.example/'},{partnerOrigin:'https://partner.example.com/path'},{deadlineAt:'invalid'},{checks:[{key:'token',challenge:'x',expiresAt:new Date().toISOString()}]}])
    assert.throws(()=>validateJob({...job(),...change}));
});
test('partner or same-origin child cannot spoof a trusted Portal execution context',()=>{
  const value=job(),context={origin:value.portalOrigin,auxData:{frameId:'main',isDefault:true}},payload=JSON.stringify({type:'bridge.connected',sessionId:value.sessionId});
  assert.equal(validateSignal(payload,context,'main',value).type,'bridge.connected');
  assert.equal(validateSignal(payload,{...context,origin:value.partnerOrigin},'main',value),null);
  assert.equal(validateSignal(payload,{...context,auxData:{frameId:'child',isDefault:true}},'main',value),null);
  assert.equal(validateSignal(JSON.stringify({type:'bridge.connected',sessionId:value.sessionId,status:'Passed'}),context,'main',value),null);
});
test('runtime proof needs actual nested host and exact partner frame, never just iframe load',()=>{
  const value=job(),tree={frame:{url:value.portalOrigin+'/developers'},childFrames:[{frame:{url:value.hostOrigin+'/sandbox-host/'+value.sessionId+'/run'},childFrames:[{frame:{url:value.websiteUrl,securityOrigin:value.partnerOrigin}}]}]};
  assert.equal(hasPartnerFrame(tree,value),true);
  tree.childFrames[0].childFrames[0].frame.securityOrigin='https://evil.example';assert.equal(hasPartnerFrame(tree,value),false);
  tree.childFrames[0].childFrames=[];assert.equal(hasPartnerFrame(tree,value),false);
});
