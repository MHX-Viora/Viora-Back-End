import assert from 'node:assert/strict';
import {readFile,writeFile,mkdir} from 'node:fs/promises';
import {join} from 'node:path';
import {createHash,X509Certificate} from 'node:crypto';
import {setTimeout as delay} from 'node:timers/promises';
import {startPortal} from './portal.fixture.mjs';
import {startSsoPartner} from './partner-sso.fixture.mjs';
import {launchChrome} from './cdp.mjs';
import {fixtureFetch} from './fixture-fetch.mjs';
import {executorClient,executeJob} from './executor.mjs';

const directory=process.argv[2],config=JSON.parse(await readFile(join(directory,'fixture.json'),'utf8'));
let browser,portal,partner;
try{
  portal=await startPortal(directory,config);partner=await startSsoPartner(directory,config);
  const certificate=new X509Certificate(await readFile(join(directory,'tls-cert.pem')));
  browser=await launchChrome('C:/Program Files/Google/Chrome/Application/chrome.exe',[
    '--ignore-certificate-errors-spki-list='+createHash('sha256').update(certificate.publicKey.export({type:'spki',format:'der'})).digest('base64'),
    '--host-resolver-rules=MAP partner.ankt.example.com 127.0.0.1, MAP identity.ankt.example.com 127.0.0.1','--no-proxy-server']);
  const cdp=browser.client,{targetId}=await cdp.send('Target.createTarget',{url:'about:blank'}),{sessionId}=await cdp.send('Target.attachToTarget',{targetId,flatten:true});
  const call=(method,params)=>cdp.send(method,params,sessionId),related=new Set();
  const remove=cdp.on(event=>{if(event.method==='Target.attachedToTarget'&&(event.sessionId===sessionId||related.has(event.sessionId))){
    const child=event.params.sessionId;related.add(child);void cdp.send('Target.setAutoAttach',{autoAttach:true,waitForDebuggerOnStart:false,flatten:true},child).catch(()=>{});
  }if(event.method==='Target.detachedFromTarget')related.delete(event.params.sessionId);});
  await call('Target.setAutoAttach',{autoAttach:true,waitForDebuggerOnStart:false,flatten:true});
  await call('Page.enable');await call('Runtime.enable');
  const read=async(expression,rpc=call)=>(await rpc('Runtime.evaluate',{expression,returnByValue:true})).result?.value;
  async function wait(expression,rpc=call){for(let i=0;i<200;i++){if(await read(expression,rpc))return;await delay(200);}
    console.log(JSON.stringify(await read(`({path:location.pathname,heading:document.querySelector('h1')?.textContent,errors:[...document.querySelectorAll('[role=alert]')].map(x=>x.textContent.slice(0,180))})`)));
    try{console.log('FIXTURE_PARTNER_STATE_'+await read(`document.querySelector('#status')?.textContent`,rpc));}catch{}
    throw new Error('Portal E2E state timed out');}
  async function click(expression,rpc=call){
    await rpc('DOM.getDocument',{depth:0});
    const result=await rpc('Runtime.evaluate',{expression});assert.ok(result.result?.objectId,'Expected visible control');
    const {nodeId}=await rpc('DOM.requestNode',{objectId:result.result.objectId});
    await rpc('DOM.scrollIntoViewIfNeeded',{nodeId});const {model}=await rpc('DOM.getBoxModel',{nodeId});
    const points=model.content,x=(points[0]+points[4])/2,y=(points[1]+points[5])/2;
    await rpc('Input.dispatchMouseEvent',{type:'mousePressed',x,y,button:'left',clickCount:1});await rpc('Input.dispatchMouseEvent',{type:'mouseReleased',x,y,button:'left',clickCount:1});
  }
  async function type(selector,text){const {root}=await call('DOM.getDocument',{depth:0});const {nodeId}=await call('DOM.querySelector',{nodeId:root.nodeId,selector});
    assert.ok(nodeId,'Expected login field');await call('DOM.focus',{nodeId});await call('Input.insertText',{text});}
  async function keyboard(expression,key='Enter'){
    await call('DOM.getDocument',{depth:0});const result=await call('Runtime.evaluate',{expression});
    const {nodeId}=await call('DOM.requestNode',{objectId:result.result.objectId});await call('DOM.focus',{nodeId});
    const code={Enter:13,ArrowDown:40,Home:36}[key];assert.ok(code);
    await call('Input.dispatchKeyEvent',{type:'keyDown',key,code:key,...(key==='Enter'?{text:'\r',unmodifiedText:'\r'}:{}),windowsVirtualKeyCode:code,nativeVirtualKeyCode:code});
    await call('Input.dispatchKeyEvent',{type:'keyUp',key,code:key,windowsVirtualKeyCode:code});
  }
  async function partnerTarget(){
    for(let attempt=0;attempt<100;attempt++){
      for(const id of related){const rpc=(method,params)=>cdp.send(method,params,id);
        try{if(await read('location.origin',rpc)==='https://partner.ankt.example.com'&&await read(`document.body.dataset.fixtureReady==='true'`,rpc))return rpc;}catch{/* Detached frame. */}
      }await delay(100);
    }throw new Error('Expected actual partner execution target');
  }
  async function hostTarget(){
    const find=tree=>{if(tree.frame.url.startsWith(config.sandboxHostOrigin+'/'))return tree.frame.id;for(const child of tree.childFrames??[]){const id=find(child);if(id)return id;}};
    for(const rpc of [call,...[...related].map(id=>(method,params)=>cdp.send(method,params,id))]){
      try{const {frameTree}=await rpc('Page.getFrameTree'),frameId=find(frameTree);if(!frameId)continue;
        const {executionContextId}=await rpc('Page.createIsolatedWorld',{frameId,worldName:'ankt-fixture-dom'});
        const bound=(method,params)=>rpc(method,method==='Runtime.evaluate'?{...params,contextId:executionContextId}:params);
        if(await read('location.origin',bound)===config.sandboxHostOrigin)return bound;
      }catch{/* Detached frame. */}
    }throw new Error('Expected actual host execution frame');
  }
  async function authorize(partnerRpc,decision='allow',first=false,consent=false){
    assert.equal(await read(`document.body.dataset.fixtureReady==='true'`,partnerRpc),true);
    const completedBefore=partner.authorizations();
    const selector=config.partnerClientType==='Public'?'#login':'#popup-login';
    const previous=new Set((await cdp.send('Target.getTargets')).targetInfos.map(x=>x.targetId));
    await click(`document.querySelector(${JSON.stringify(selector)})`,partnerRpc);
    if(!first&&!consent&&decision==='allow'){
      for(let i=0;i<100;i++){
        const targets=(await cdp.send('Target.getTargets')).targetInfos.filter(x=>x.type==='page'&&!previous.has(x.targetId));
        for(const target of targets){try{
          const attached=await cdp.send('Target.attachToTarget',{targetId:target.targetId,flatten:true});
          assert.notEqual(await read(`!!document.querySelector('button[name=decision][value=allow]')`,(method,params)=>cdp.send(method,params,attached.sessionId)),true,'Unexpected repeated consent');
        }catch(error){if(error.code==='ERR_ASSERTION')throw error;}}
        const active=await partnerTarget();
        if(await read(`document.querySelector('#status')?.textContent==='authenticated'`,active)&&
          (config.partnerClientType==='Public'||partner.authorizations()>completedBefore))return;
        await delay(100);
      }throw new Error('Repeat SSO did not revalidate the actual partner session');
    }
    let target;
    for(let i=0;i<100&&!target;i++){
      target=(await cdp.send('Target.getTargets')).targetInfos.find(x=>x.type==='page'&&x.targetId!==targetId&&x.url.startsWith(config.issuer));
      if(!target)await delay(100);
    }assert.ok(target,'Expected real ANKT popup');
    const attachment=await cdp.send('Target.attachToTarget',{targetId:target.targetId,flatten:true});
    const popup=(method,params)=>cdp.send(method,params,attachment.sessionId);
    if(first){await wait(`!!document.querySelector('form[action="/fixture/login"] button')`,popup);await click(`document.querySelector('form[action="/fixture/login"] button')`,popup);}
    if(first||consent||decision==='deny'){
      await wait(`!!document.querySelector('button[name=decision][value=allow]')`,popup);
      assert.equal(await read(`document.body.textContent.includes('Sandbox')`,popup),true);
      await click(`document.querySelector('button[name=decision][value=${decision}]')`,popup);
    }
    if(decision==='deny'){
      if(config.partnerClientType==='Public')await wait(`document.querySelector('#status')?.textContent==='request_failed'`,partnerRpc);
      await cdp.send('Target.closeTarget',{targetId:target.targetId}).catch(()=>{});
    }
  }
  const button=text=>`[...document.querySelectorAll('button')].find(x=>x.textContent===${JSON.stringify(text)})`;
  const fixture=async(path,method='GET')=>{const response=await fixtureFetch(new URL('/fixture/'+path,config.issuer),{method,headers:{Authorization:'SandboxExecutor '+config.executorKey}});assert.ok(response.ok);return response.status===204?null:response.json();};
  const preserved=await fixture('state-summary');assert.equal(preserved.productionCount,1);assert.equal(preserved.socialCount,1);
  assert.equal(preserved.consentCount,1);assert.equal(preserved.tokenCount,1);
  const appId='app_'+'e'.repeat(43),url=config.sandboxPortalOrigin+'/developers/console/apps/'+appId+'/sandbox';
  await call('Page.navigate',{url});await wait(`!!document.querySelector('input[type=email]')`);
  console.log('PROTECTED_ROUTE_REQUIRES_DEVELOPER_LOGIN');
  await type('input[type=email]','executor@fixture.test');await type('input[type=password]','Sandbox fixture password 2026!');await click(`document.querySelector('button[type=submit]')`);
  await wait(`location.pathname==='/developers/console'`);console.log('REAL_DEVELOPER_BFF_LOGIN');
  await call('Page.navigate',{url});await wait(`!!${button('Bắt đầu phiên Sandbox')}`);
  console.log('OWNED_SANDBOX_ROUTE_LOADED');
  await keyboard(button('Bắt đầu phiên Sandbox'));await wait(`document.querySelector('.mini-sandbox-preview iframe')!==null`);
  await wait(`document.body.textContent.includes('Đã kết nối bridge')`);console.log('REAL_PORTAL_HOST_PARTNER_BRIDGE');
  await click(button('Chạy 18 kiểm tra'));await wait(`!!${button('Hủy lượt kiểm tra')}`);await click(button('Hủy lượt kiểm tra'));
  await wait(`document.body.textContent.includes('Đã hủy')`);await wait(`document.activeElement?.textContent==='Chạy 18 kiểm tra'`);console.log('RUN_CANCEL_AND_FOCUS_RESTORED');
  await keyboard(button('Tải diagnostics'));await wait(`document.querySelector('.sandbox-diagnostics-list li')!==null`);console.log('LIVE_DIAGNOSTICS_VISIBLE');
  let partnerRpc=await partnerTarget();
  const request=executorClient(config.issuer,config.executorKey,fixtureFetch);
  await authorize(partnerRpc,'deny',true);
  assert.deepEqual(await (await fixtureFetch(new URL('/fixture/protocol-summary',config.issuer),{headers:{Authorization:'SandboxExecutor '+config.executorKey}})).json(),[]);
  console.log('REAL_FIRST_CONSENT_DENIED_WITHOUT_PROTOCOL_PROOF');
  // Start a new real run. Browser observations alone cannot supply its protocol results.
  await click(button('Chạy 18 kiểm tra'));await wait(`!!${button('Hủy lượt kiểm tra')}`);
  let jobs=[];
  for(let i=0;i<30&&!jobs.length;i++){jobs=await request('runs');if(!jobs.length)await delay(200);}
  assert.equal(jobs.length,1);
  const trusted=executeJob(jobs[0],{chrome:'C:/Program Files/Google/Chrome/Application/chrome.exe',request,fixtureDirectory:directory,observe:value=>console.log('TRUSTED_'+value)});
  void trusted.catch(()=>{});
  if(config.partnerClientType==='Public')await wait(`document.querySelector('#run-ready')?.textContent==='ready'`,partnerRpc);
  await authorize(partnerRpc,'allow',false,true);
  // Denial did not persist consent, so the next popup requires a fresh allow decision.
  const ssoPreserved=await fixture('sso-state');assert.ok(ssoPreserved.count>0);
  if(config.restrictPartnerCookie){
    assert.equal(config.partnerClientType,'Confidential');
    await wait(`document.querySelector('#status')?.textContent.startsWith('embedded_session_unavailable')`,partnerRpc);
    assert.equal((await trusted).status,'Passed');console.log('TOP_LEVEL_SSO_IS_NOT_EMBEDDED_COOKIE_SUCCESS');
  }else{
  partnerRpc=await partnerTarget();await wait(`document.querySelector('#status')?.textContent==='authenticated'`,partnerRpc);
  assert.equal((await trusted).status,'Passed');console.log('TRUSTED_RUNTIME_BRIDGE_AND_REAL_'+config.partnerClientType.toUpperCase()+'_SSO');
  await wait(`document.querySelector('.sandbox-readiness div:last-child dd')?.textContent==='Đủ bằng chứng'`);
  console.log('ALL_18_CHECKS_AND_FRESH_REVIEW_READINESS');
  await keyboard(button('Tải lại website'));await wait(`document.body.textContent.includes('Đã kết nối bridge')`);
  partnerRpc=await partnerTarget();
  if(config.partnerClientType==='Public')await wait(`document.querySelector('#status')?.textContent==='unauthenticated'`,partnerRpc);
  else await wait(`document.querySelector('#status')?.textContent==='authenticated'`,partnerRpc);
  await authorize(partnerRpc);partnerRpc=await partnerTarget();await wait(`document.querySelector('#status')?.textContent==='authenticated'`,partnerRpc);
  console.log('REAL_PREVIEW_RELOAD_AND_REPEAT_SSO_WITHOUT_CONSENT');
  const screenshots=join(import.meta.dirname,'../../.codex-tmp/stage6-portal-screenshots');await mkdir(screenshots,{recursive:true});
  for(const [width,height] of [[320,812],[375,812],[768,1024],[1440,1000]]){
    await call('Emulation.setDeviceMetricsOverride',{width,height,deviceScaleFactor:1,mobile:width<600});
    assert.equal(await read('document.documentElement.scrollWidth<=innerWidth'),true,'Portal overflow');
    if(width!==320)await writeFile(join(screenshots,config.partnerClientType+'-'+width+'.png'),Buffer.from((await call('Page.captureScreenshot',{format:'png',captureBeyondViewport:false})).data,'base64'));
  }console.log('REAL_PORTAL_RESPONSIVE_320_375_768_1440');
  const size=`document.querySelector('[aria-label="Điều khiển Sandbox"] select')`;
  await keyboard(size,'Home');await keyboard(size,'ArrowDown');await wait(`!!document.querySelector('.mini-sandbox-tablet')`);
  assert.equal(await read(`document.querySelector('.mini-sandbox-preview').getBoundingClientRect().width`),768);
  await keyboard(size,'ArrowDown');await wait(`!!document.querySelector('.mini-sandbox-desktop')`);
  await keyboard(size,'Home');await wait(`!!document.querySelector('.mini-sandbox-mobile')`);console.log('REAL_PREVIEW_DEVICE_SWITCHING_BY_KEYBOARD');
  if(config.partnerClientType==='Public'){
    const hostRpc=await hostTarget();
    await click(`document.querySelector('#external')`,partnerRpc);await wait(`!!document.querySelector('dialog[open]')`,hostRpc);
    await click(`document.querySelector('dialog button')`,hostRpc);await wait(`document.querySelector('#bridge-result')?.textContent==='cancelled'`,partnerRpc);
    await click(`document.querySelector('#external')`,partnerRpc);await wait(`!!document.querySelector('dialog[open]')`,hostRpc);
    const before=new Set((await cdp.send('Target.getTargets')).targetInfos.map(x=>x.targetId));
    await click(`document.querySelector('dialog a')`,hostRpc);
    let external;
    for(let i=0;i<100&&!external;i++){external=(await cdp.send('Target.getTargets')).targetInfos.find(x=>x.type==='page'&&!before.has(x.targetId)&&x.url==='https://partner.ankt.example.com/help');if(!external)await delay(100);}
    assert.ok(external);const attached=await cdp.send('Target.attachToTarget',{targetId:external.targetId,flatten:true});
    assert.equal(await read(`window.opener===null&&document.referrer===''`,(method,params)=>cdp.send(method,params,attached.sessionId)),true);
    await cdp.send('Target.closeTarget',{targetId:external.targetId});console.log('AUTHENTICATED_EXTERNAL_CANCEL_CONFIRM_NO_OPENER_REFERRER');
    await click(`document.querySelector('#close')`,partnerRpc);await wait(`!!${button('Bắt đầu phiên Sandbox')}`);
    await wait(`document.activeElement?.textContent==='Bắt đầu phiên Sandbox'`);assert.equal((await fixture('ended-check')).rejected,true);
    console.log('REAL_SDK_CLOSE_REVOKED_SESSION_AND_RESTORED_FOCUS');
    await keyboard(button('Bắt đầu phiên Sandbox'));await wait(`document.body.textContent.includes('Đã kết nối bridge')`);
  }
  }
  await keyboard(button('Dừng phiên'));await wait(`!!${button('Bắt đầu phiên Sandbox')}`);
  await wait(`document.activeElement?.textContent==='Bắt đầu phiên Sandbox'`);console.log('SANDBOX_SESSION_STOPPED_AND_FOCUS_RESTORED');
  assert.equal((await fixture('ended-check')).rejected,true);console.log('STOPPED_SESSION_BACKEND_REJECTED');
  await keyboard(button('Bắt đầu phiên Sandbox'));await wait(`document.body.textContent.includes('Đã kết nối bridge')`);
  await fixture('expire-session','POST');await wait(`!!${button('Bắt đầu phiên Sandbox')}`);
  await wait(`document.activeElement?.textContent==='Bắt đầu phiên Sandbox'`);
  assert.equal((await fixture('ended-check')).rejected,true);console.log('EXPIRED_SESSION_UI_AND_BACKEND_REJECTED');
  assert.deepEqual(await fixture('state-summary'),preserved);assert.deepEqual(await fixture('sso-state'),ssoPreserved);
  console.log('NONEMPTY_PRODUCTION_REGISTRY_CONSENT_TOKEN_PUBLICATION_AND_ANKT_SESSIONS_UNCHANGED');
}finally{
  const summary=await fixtureFetch(new URL('/fixture/protocol-summary',config.issuer),{headers:{Authorization:'SandboxExecutor '+config.executorKey}}).catch(()=>null);
  if(summary?.ok)console.log(JSON.stringify(await summary.json()));
  await browser?.close();await partner?.close();await portal?.close();
  await fixtureFetch(new URL('/fixture/executor-shutdown',config.issuer),{method:'POST',headers:{Authorization:'SandboxExecutor '+config.executorKey}}).catch(()=>{});
}
