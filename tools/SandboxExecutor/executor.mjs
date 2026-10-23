import {createHmac,X509Certificate,createHash} from 'node:crypto';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
import {pathToFileURL} from 'node:url';
import {launchChrome} from './cdp.mjs';
import {validateJob,validateSignal,hasPartnerFrame,mergeFrameTrees} from './protocol.mjs';

export function executorClient(issuer,key,fetcher=fetch){
  const origin=new URL(issuer);if(origin.protocol!=='https:'||origin.username||origin.password||origin.search||origin.hash||origin.pathname!=='/'||Buffer.from(key,'base64').length<32)throw new Error('Invalid executor configuration');
  return async(path,body,value)=>{
    const signature=body?createHmac('sha256',Buffer.from(key,'base64')).update(body.challenge+'\n'+value).digest('hex'):undefined;
    const response=await fetcher(new URL('/internal/sandbox-executor/'+path,origin),{method:body?'POST':'GET',redirect:'error',credentials:'omit',signal:AbortSignal.timeout(10000),
      headers:{Authorization:'SandboxExecutor '+key,...(body?{'Content-Type':'application/json','X-Ankt-Sandbox-Signature':signature}:{})},body:body?JSON.stringify(body):undefined});
    if(!response.ok)throw new Error('Executor request rejected ('+response.status+')');return response.status===204?null:response.json();
  };
}
export async function executeJob(input,{chrome,request,fixtureDirectory,observe=()=>{}}={}){
  const job=validateJob(input),args=[];
  if(fixtureDirectory){
    if(job.portalOrigin!=='https://portal.ankt.localhost:5494'||job.hostOrigin!=='https://sandbox.ankt.localhost:5493'||job.partnerOrigin!=='https://partner.ankt.example.com')throw new Error('Invalid fixture origins');
    const certificate=new X509Certificate(await readFile(join(fixtureDirectory,'tls-cert.pem')));
    args.push('--ignore-certificate-errors-spki-list='+createHash('sha256').update(certificate.publicKey.export({type:'spki',format:'der'})).digest('base64'),
      '--host-resolver-rules=MAP partner.ankt.example.com 127.0.0.1, MAP identity.ankt.example.com 127.0.0.1','--no-proxy-server');
  }
  const browser=await launchChrome(chrome,args);let remove,stopTimer;
  try{
    const cdp=browser.client,{targetId}=await cdp.send('Target.createTarget',{url:'about:blank'});
    // Only the fixed disposable loopback fixture needs this permission; production gets no grant.
    if(fixtureDirectory)await cdp.send('Browser.grantPermissions',{origin:job.portalOrigin,permissions:['loopbackNetwork']});
    const {sessionId}=await cdp.send('Target.attachToTarget',{targetId,flatten:true});
    const call=(method,params)=>cdp.send(method,params,sessionId);await call('Page.enable');
    const {frameTree}=await call('Page.getFrameTree');const main=frameTree.frame.id,contexts=new Map(),related=new Set(),hostRequests=new Map();let contextId,launched=false,finished=false;
    const launcherUrl=job.portalOrigin+'/developers/console/apps/'+encodeURIComponent(job.appId)+'/sandbox';let harnessDelivered=false,primaryCommitted=false;
    const outcome=new Promise(resolve=>{
      const timer=setTimeout(()=>{finished=true;resolve(false);},Math.max(1,Math.min(35000,...job.checks.map(x=>Date.parse(x.expiresAt)-Date.now()-2000))));
      stopTimer=()=>clearTimeout(timer);
      remove=cdp.on(event=>{
        if(finished)return;
        if(event.method==='Target.attachedToTarget'&&(event.sessionId===sessionId||related.has(event.sessionId))){
          if(related.size>=32){observe('CONTROLLED_FRAME_LIMIT');finished=true;clearTimeout(timer);resolve(false);return;}
          const child=event.params.sessionId;related.add(child);
          void cdp.send('Target.setAutoAttach',{autoAttach:true,waitForDebuggerOnStart:false,flatten:true},child).catch(()=>{});return;
        }
        if(event.method==='Target.detachedFromTarget')related.delete(event.params.sessionId);
        if(event.sessionId!==sessionId||finished)return;
        if(event.method==='Network.requestWillBeSent'&&event.params.request.url.startsWith(job.hostOrigin+'/')&&hostRequests.size<32)hostRequests.set(event.params.requestId,event.params.type);
        if(event.method==='Network.responseReceived'&&hostRequests.has(event.params.requestId)&&event.params.response.status>=400)observe('CONTROLLED_HOST_HTTP_'+event.params.response.status);
        if(event.method==='Network.loadingFailed'&&hostRequests.has(event.params.requestId)){
          observe('CONTROLLED_HOST_BLOCKED_'+(event.params.blockedReason??'NETWORK')+'_'+(/^[a-zA-Z0-9:_]+$/.test(event.params.errorText??'')?event.params.errorText:'UNKNOWN'));
          if(hostRequests.get(event.params.requestId)==='Document'){finished=true;clearTimeout(timer);resolve(false);return;}
        }
        if(event.method==='Runtime.exceptionThrown')observe('CONTROLLED_SCRIPT_ERROR');
        if(event.method==='Page.domContentEventFired'&&primaryCommitted){
          void call('Runtime.evaluate',{expression:source}).then(result=>{if(result.exceptionDetails)observe('CONTROLLED_PARENT_SCRIPT_REJECTED');}).catch(()=>{});
        }
        if(event.method==='Page.frameNavigated'&&event.params.frame.id===main){
          if(event.params.frame.url===launcherUrl){primaryCommitted=true;observe('CONTROLLED_PARENT_COMMITTED');}
          else if(primaryCommitted){observe('CONTROLLED_PARENT_NAVIGATION_REJECTED');finished=true;clearTimeout(timer);resolve(false);return;}
        }
        if(event.method==='Fetch.requestPaused'){
          const value=event.params;
          // Controlled executor parent only. Never replace actual host/partner headers or content.
          const accepted=!harnessDelivered&&value.frameId===main&&value.resourceType==='Document'&&value.request.url===launcherUrl&&value.responseStatusCode!==undefined&&!value.responseErrorReason;
          harnessDelivered||=accepted;
          observe(accepted?'CONTROLLED_PARENT_DELIVERED':'CONTROLLED_PARENT_REQUEST_REJECTED');
          void call(accepted?'Fetch.fulfillRequest':'Fetch.failRequest',accepted?{requestId:value.requestId,responseCode:200,responseHeaders:[
            {name:'Content-Type',value:'text/html; charset=utf-8'},{name:'Cache-Control',value:'no-store'},
            {name:'Content-Security-Policy',value:"default-src 'none'; frame-src "+job.hostOrigin+"; frame-ancestors 'none'"},
            {name:'Referrer-Policy',value:'no-referrer'}],body:Buffer.from('<!doctype html><html lang=en><meta charset=utf-8><title>Controlled runtime executor</title><body><h1>Controlled runtime executor</h1></body></html>').toString('base64')}:
            {requestId:value.requestId,errorReason:'BlockedByClient'}).catch(()=>{finished=true;clearTimeout(timer);resolve(false);});return;
        }
        if(event.method==='Runtime.executionContextCreated'){const context=event.params.context;if(context.origin===job.portalOrigin&&context.auxData?.frameId===main&&context.auxData?.isDefault===true)contexts.set(context.id,context);}
        if(event.method==='Runtime.executionContextsCleared')contexts.clear();
        if(event.method==='Runtime.executionContextDestroyed')contexts.delete(event.params.executionContextId);
        if(event.method!=='Runtime.bindingCalled'||event.params.name!=='anktExecutorSignal')return;
        const value=validateSignal(event.params.payload,contexts.get(event.params.executionContextId),main,job);if(!value)return;
        contextId=event.params.executionContextId;
        void (async()=>{
          if(value.type==='host.ready'&&!launched){
            observe('HOST_READY');
            launched=true;const challenge=job.checks[0].challenge;
            const grant=await request('launch-grants',{challenge,browserNonce:value.browserNonce},value.browserNonce);
            observe('LAUNCH_GRANTED');
            if(finished)return;
            const message={channel:'ankt.sandbox.host',version:1,type:'launch',sessionId:job.sessionId,browserNonce:value.browserNonce,grant:grant.grant};
            const result=await call('Runtime.evaluate',{contextId,expression:'document.querySelector("#__anktExecutorHost").contentWindow.postMessage('+JSON.stringify(message)+','+JSON.stringify(job.hostOrigin)+')'});
            if(result.exceptionDetails)throw new Error('Host launch failed');
          }
          if(value.type==='bridge.connected'&&launched){
            observe('BRIDGE_CONNECTED');
            const tree=await call('Page.getFrameTree');
            const childTrees=await Promise.all([...related].map(id=>cdp.send('Page.getFrameTree',{},id).then(x=>x.frameTree).catch(()=>null)));
            const valid=hasPartnerFrame(mergeFrameTrees(tree.frameTree,childTrees.filter(Boolean)),job);
            observe(valid?'FRAME_VERIFIED':'FRAME_UNVERIFIED');
            finished=true;clearTimeout(timer);resolve(valid);
          }
        })().catch(()=>{observe('EXECUTION_REJECTED');finished=true;clearTimeout(timer);resolve(false);});
      });
    });
    await call('Runtime.enable');await call('Runtime.addBinding',{name:'anktExecutorSignal'});
    await call('Network.enable');
    await call('Target.setAutoAttach',{autoAttach:true,waitForDebuggerOnStart:false,flatten:true});
    // Intercept after the real TLS response: preserve the browser's actual address-space attribution.
    await call('Fetch.enable',{patterns:[{urlPattern:launcherUrl,resourceType:'Document',requestStage:'Response'}]});
    const source=`if(location.origin===${JSON.stringify(job.portalOrigin)}&&window.top===window&&!document.querySelector('#__anktExecutorHost')){
      const launch=()=>{if(document.querySelector('#__anktExecutorHost'))return;
        const frame=document.createElement('iframe');frame.id='__anktExecutorHost';frame.title='Controlled Sandbox executor';frame.referrerPolicy='no-referrer';
        window.addEventListener('message',event=>{const value=event.data;
          if(event.origin!==${JSON.stringify(job.hostOrigin)}||event.source!==frame.contentWindow||!value||value.channel!=='ankt.sandbox.host'||value.version!==1||value.sessionId!==${JSON.stringify(job.sessionId)})return;
          if(value.type==='host.ready')anktExecutorSignal(JSON.stringify({type:value.type,sessionId:value.sessionId,browserNonce:value.browserNonce}));
          if(value.type==='bridge.connected')anktExecutorSignal(JSON.stringify({type:value.type,sessionId:value.sessionId}));
        });frame.src=${JSON.stringify(job.hostOrigin+'/sandbox-host/'+job.sessionId)};document.body.append(frame);
      };if(document.readyState==='loading')document.addEventListener('DOMContentLoaded',launch,{once:true});else launch();}`;
    await call('Page.addScriptToEvaluateOnNewDocument',{source});
    await call('Page.navigate',{url:launcherUrl});
    const passed=await outcome;
    if(!passed){
      const state=await call('Runtime.evaluate',{expression:`JSON.stringify({origin:location.origin===${JSON.stringify(job.portalOrigin)},frame:!!document.querySelector('#__anktExecutorHost'),binding:typeof anktExecutorSignal==='function',loaded:document.readyState==='complete'})`,returnByValue:true}).catch(()=>null);
      if(state?.result?.value)observe('CONTROLLED_PARENT_STATE_'+state.result.value);
    }
    for(const check of job.checks)await request('evidence',{challenge:check.challenge,status:passed?'Passed':'Failed'},passed?'Passed':'Failed');
    return {runId:job.runId,status:passed?'Passed':'Failed'};
  }finally{stopTimer?.();remove?.();await browser.close();}
}
async function main(){
  const request=executorClient(process.env.ANKT_SANDBOX_ISSUER,process.env.ANKT_SANDBOX_EXECUTOR_KEY);
  const jobs=await request('runs');if(!Array.isArray(jobs)||jobs.length>8)throw new Error('Invalid executor response');
  for(const job of jobs)console.log(JSON.stringify(await executeJob(job,{chrome:process.env.ANKT_SANDBOX_CHROME,request,fixtureDirectory:process.env.ANKT_SANDBOX_FIXTURE_DIRECTORY})));
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)main().catch(()=>{console.error('Sandbox executor failed; no readiness proof claimed.');process.exitCode=1;});
