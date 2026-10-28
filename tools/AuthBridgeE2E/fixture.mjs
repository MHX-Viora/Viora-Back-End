import assert from 'node:assert/strict';
import https from 'node:https';
import http from 'node:http';
import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createRequire} from 'node:module';
import {setTimeout as delay} from 'node:timers/promises';
import {createHash,X509Certificate,randomBytes} from 'node:crypto';
import {launchChrome} from '../SandboxExecutor/cdp.mjs';
import {buildNts,runNtsBrowser} from './nts-browser.fixture.mjs';
const ntsMode=process.env.ANKT_E2E_NTS==='true';
let ntsExchanges=0;
const directory=process.argv[2],config=JSON.parse(await readFile(join(directory,'bridge-fixture.json'),'utf8'));
const frontend=resolve(import.meta.dirname,'../../../viora'),require=createRequire(join(frontend,'package.json'));
const {build}=require('esbuild'),{createDeveloperHandler}=require(join(frontend,'server/developer/handler.cjs'));
const tls={cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))},ca=await readFile(join(directory,'root-ca.pem'));
function localFetch(input,init={}){
  const url=new URL(String(input));assert.equal(url.origin,config.api);
  return new Promise((resolve,reject)=>{const body=init.body instanceof URLSearchParams?init.body.toString():init.body;const headers=Object.fromEntries(new Headers(init.headers));headers.Host=url.host;
    if(body!=null)headers['Content-Length']=Buffer.byteLength(body);
    const request=https.request({hostname:'127.0.0.1',port:5490,path:url.pathname+url.search,method:init.method??'GET',headers,ca,servername:url.hostname,signal:init.signal},response=>{
      const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>{const headers=new Headers();for(let i=0;i<response.rawHeaders.length;i+=2)headers.append(response.rawHeaders[i],response.rawHeaders[i+1]);
        resolve(new Response([204,304].includes(response.statusCode)?null:Buffer.concat(chunks),{status:response.statusCode,headers}));});});request.on('error',reject);request.end(body);
  });
}
const stub=join(directory,'session-store.ts');await writeFile(stub,'export async function getSession(){return {accessToken:'+JSON.stringify(config.socialToken)+'};} export function subscribeSessionInvalidation(){return ()=>{};}');
const options={bundle:true,write:false,jsx:'automatic',nodePaths:[join(frontend,'node_modules')],alias:{react:join(frontend,'node_modules/react'),'react-dom':join(frontend,'node_modules/react-dom')},
  define:{'process.env.NODE_ENV':'"development"','process.env.EXPO_PUBLIC_API_URL':JSON.stringify(config.api)}};
const portalBundle=await build({...options,entryPoints:[join(import.meta.dirname,'portal.fixture.jsx')],outfile:'portal.js',plugins:[{name:'fixture-session',setup(b){b.onResolve({filter:/stores\/session-store$/},()=>({path:stub}));}}]});
const miniBundle=await build({...options,entryPoints:[join(import.meta.dirname,'miniapp.fixture.ts')],outfile:'miniapp.js'});
const assets=new Map([...portalBundle.outputFiles,...miniBundle.outputFiles].map(file=>['/'+file.path.split(/[\\/]/).at(-1),file.contents]));
if(ntsMode)for(const [key,value]of await buildNts(config,options))assets.set(key,value);
const partnerAccounts=new Map(),partnerSessions=new Map();
const sdkManifest=JSON.parse(await readFile(join(frontend,'public/sdk/manifest.json'),'utf8'));
const sdkBytes=await readFile(join(frontend,'public/sdk',sdkManifest.filename));
assets.set('/ankt-miniapp.js',sdkBytes);assets.set(sdkManifest.url,sdkBytes);
assets.set('/sdk/manifest.json',Buffer.from(JSON.stringify(sdkManifest)));
const bff=createDeveloperHandler({siteOrigin:config.portal,apiOrigin:config.api,production:true,bffSecret:config.bffKey,clientIp:'127.0.0.1'},localFetch);
const portalHandler=async(req,res)=>{try{
  const url=new URL(req.url,config.portal);if(url.pathname.startsWith('/developers/api/')){await bff(req,res);return;}
  res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
  res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' https: data:; connect-src 'self' https:; frame-src https:; frame-ancestors 'none'; object-src 'none'");
  if(assets.has(url.pathname)){const bytes=assets.get(url.pathname);res.setHeader('Content-Type',url.pathname.endsWith('.css')?'text/css':url.pathname.endsWith('.json')?'application/json':'text/javascript');res.setHeader('Content-Length',bytes.length);res.end(req.method==='HEAD'?undefined:bytes);return;}
  res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1"><link rel=icon href="data:,"><link rel=stylesheet href=/portal.css><div id=root></div><script src=/portal.js></script></html>');
}catch{res.writeHead(500);res.end('Portal fixture failed');}};
const portal=https.createServer(tls,portalHandler);
const localOrigin='http://localhost:5495';
const localBff=createDeveloperHandler({siteOrigin:localOrigin,apiOrigin:config.api,production:false,bffSecret:config.bffKey,clientIp:'127.0.0.1'},localFetch);
const localPortal=http.createServer(async(req,res)=>{
  if(new URL(req.url,localOrigin).pathname.startsWith('/developers/api/')){await localBff(req,res);return;}
  await portalHandler(req,res);
});
const partner=https.createServer(tls,async(req,res)=>{
  if(!ntsMode)assert.equal(req.headers.authorization,undefined);
  assert.ok(!req.url.includes(config.socialToken));
  res.setHeader('Cache-Control','no-store');res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self'; connect-src 'self' https://identity.ankt.example.com:5490; frame-ancestors https://portal.ankt.localhost:5494 http://localhost:5495");
  if(ntsMode){
    res.setHeader('Content-Security-Policy',"default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; font-src 'self' data:; connect-src 'self' https://identity.ankt.example.com:5490; frame-ancestors https://portal.ankt.localhost:5494");
    const match=req.url.match(/^\/fixture\/([ab])\/api\/(auth\/ankt\/exchange|users\/me\/select-role)$/);
    if(match&&req.method==='POST'){
      if(match[2]==='auth/ankt/exchange')ntsExchanges++;
      const chunks=[];for await(const chunk of req)chunks.push(chunk);
      const response=await localFetch(config.api+'/fixture/nts/app_'+match[1].repeat(43)+'/'+(match[2]==='auth/ankt/exchange'?'exchange':'select-role'),
        {method:'POST',headers:{'Content-Type':'application/json',...(req.headers.authorization?{Authorization:req.headers.authorization}:{})},body:Buffer.concat(chunks)});
      res.statusCode=response.status;res.setHeader('Content-Type','application/json');res.end(await response.text());return;
    }
    if(assets.has(req.url)){res.setHeader('Content-Type','text/javascript');res.end(assets.get(req.url));return;}
    if(req.url==='/ankt-miniapp.js'){res.setHeader('Content-Type','text/javascript');res.end(sdkBytes);return;}
    const key=req.url.startsWith('/b')?'b':'a';
    res.setHeader('Content-Type','text/html');res.end('<!doctype html><html lang=vi><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1"><div id=root></div><script src=/nts-'+key+'.js></script></html>');return;
  }
  if(req.url==='/api/ankt/login'&&req.method==='POST'){
    const chunks=[];for await(const chunk of req)chunks.push(chunk);
    const body=JSON.parse(Buffer.concat(chunks).toString());
    const exchanged=await localFetch(config.api+'/api/miniapp/auth/partner/exchange',{method:'POST',
      headers:{'Content-Type':'application/json',Authorization:'Basic '+Buffer.from(body.clientId+':'+config.partnerSecret).toString('base64')},body:JSON.stringify({code:body.code})});
    assert.equal(exchanged.status,200);const identity=await exchanged.json();
    assert.equal(identity.miniAppId,body.clientId);assert.ok(['Sandbox','Production'].includes(identity.environment));
    const accountKey=JSON.stringify([identity.miniAppId,identity.environment,identity.user.id]);
    if(!partnerAccounts.has(accountKey))partnerAccounts.set(accountKey,randomBytes(16).toString('hex'));
    const session=randomBytes(32).toString('base64url');
    partnerSessions.set(session,{accountId:partnerAccounts.get(accountKey),grantId:identity.sessionId,expiresAt:identity.expiresAt});
    res.setHeader('Content-Type','application/json');res.end(JSON.stringify({user:identity.user,environment:identity.environment,partnerSession:session}));return;
  }
  if(req.url==='/miniapp.js'){res.setHeader('Content-Type','text/javascript');res.end(assets.get('/miniapp.js'));return;}
  if(req.url==='/ankt-miniapp.js'){res.setHeader('Content-Type','text/javascript');res.end(sdkBytes);return;}
  if(req.url==='/miniapp.css'){res.setHeader('Content-Type','text/css');res.end('body{font-family:Arial,sans-serif;background:#f4f8ff;color:#173452;margin:0;padding:24px}main{background:white;border:1px solid #dce7f5;border-radius:12px;padding:24px;max-width:520px;margin:24px auto}small{color:#0866e6;letter-spacing:2px}h1{font-size:24px;overflow-wrap:anywhere}p{line-height:1.6;overflow-wrap:anywhere}#kind{color:#21653c}');return;}
  res.setHeader('Content-Type','text/html');res.end('<!doctype html><html><meta charset=utf-8><link rel=icon href="data:,"><link rel=stylesheet href=/miniapp.css><main><small>ANKT MINI APP</small><h1 id=name>Đang đăng nhập…</h1><p id=username></p><p id=kind></p><pre id=profile hidden></pre><pre id=session hidden></pre><p id=error></p></main><script src=/ankt-miniapp.js></script><script src=/miniapp.js></script></html>');
});
await Promise.all([new Promise(r=>portal.listen(5494,'127.0.0.1',r)),new Promise(r=>localPortal.listen(5495,'127.0.0.1',r)),new Promise(r=>partner.listen(443,'127.0.0.1',r))]);
let browser;
try{
  const spki=createHash('sha256').update(new X509Certificate(tls.cert).publicKey.export({type:'spki',format:'der'})).digest('base64');
  browser=await launchChrome('C:/Program Files/Google/Chrome/Application/chrome.exe',['--ignore-certificate-errors-spki-list='+spki,'--no-proxy-server','--host-resolver-rules=MAP portal.ankt.localhost 127.0.0.1, MAP identity.ankt.example.com 127.0.0.1, MAP partner.ankt.example.com 127.0.0.1']);
  const cdp=browser.client;const evaluate=async(id,expression)=>(await cdp.send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:true},id)).result.value;
  const wait=async action=>{const deadline=Date.now()+30000;while(Date.now()<deadline){try{const result=await action();if(result)return result;}catch{}await delay(150);}throw Error('Browser condition timed out');};
  const sessions=new Map();
  cdp.on(event=>{if(event.method==='Target.targetDestroyed')sessions.delete(event.params.targetId);});
  const attach=async targetId=>{if(sessions.has(targetId))return sessions.get(targetId);const {sessionId}=await cdp.send('Target.attachToTarget',{targetId,flatten:true});await cdp.send('Runtime.enable',{},sessionId);await cdp.send('Page.enable',{},sessionId);await cdp.send('Network.enable',{},sessionId);sessions.set(targetId,sessionId);return sessionId;};
  let exchanges=0;cdp.on(event=>{
    if(event.method==='Network.requestWillBeSent'){
      const request=event.params.request;
      assert.ok(!JSON.stringify(request).includes(config.partnerSecret));
      if(request.url.includes('/auth/code')){assert.ok(!Object.values(request.headers).includes('Bearer '+config.socialToken));assert.ok(!request.url.includes('code='));}
      if(request.url.includes('/auth/exchange')){exchanges++;assert.ok(!Object.values(request.headers).includes('Bearer '+config.socialToken));assert.ok(!request.url.includes('code='));}
    }
    if(event.method==='Runtime.exceptionThrown')console.error('Browser script:',String(event.params.exceptionDetails.exception?.description??event.params.exceptionDetails.text).split('\n')[0].slice(0,180));
  });
  const contexts=[];
  async function tab(path,origin=config.portal){const {browserContextId}=await cdp.send('Target.createBrowserContext');contexts.push(browserContextId);const {targetId}=await cdp.send('Target.createTarget',{browserContextId,url:'about:blank'});const sessionId=await attach(targetId);await cdp.send('Page.navigate',{url:origin+path},sessionId);return{browserContextId,sessionId,targetId};}
  async function login(account,origin=config.portal){const t=await tab('/developers/console/apps/'+account.appId+'/sandbox?app='+account.appId,origin);await wait(()=>evaluate(t.sessionId,"!!document.querySelector('[name=email]')"));
    await evaluate(t.sessionId,`document.querySelector('[name=email]').value=${JSON.stringify(account.email)};document.querySelector('[name=password]').value=${JSON.stringify(account.password)};document.querySelector('form button').click();true`);return t;}
  async function profile(t){
    // Cross-site iframes are separate Chrome targets. Keep site isolation enabled
    // and attach to that target instead of evaluating in the parent's process.
    return wait(async()=>{
      const child=(await cdp.send('Target.getTargets')).targetInfos.find(info=>info.type==='iframe'&&info.browserContextId===t.browserContextId&&info.url.startsWith(config.partner));
      if(!child)return null;
      const sessionId=await attach(child.targetId);
      try{const result=await evaluate(sessionId,"document.querySelector('#profile')?.textContent");return result?JSON.parse(result):null;}
      catch(error){sessions.delete(child.targetId);throw error;}
    });}
  if(ntsMode){
    await runNtsBrowser({cdp,login,config,attach,evaluate,wait,exchanges:()=>ntsExchanges});
    const counts=await(await localFetch(config.api+'/fixture/nts/evidence')).json();
    assert.equal(counts.accounts,2);assert.equal(counts.links,2);
    console.log('PASS PostgreSQL contains exactly one account/link per app; concurrent login left no orphan account');
  }else{
  const [a,b]=await Promise.all(config.accounts.map(account=>login(account)));let profiles;
  try{profiles=await Promise.all([profile(a),profile(b)]);}catch(error){console.error('Portal A:',await evaluate(a.sessionId,'document.body.innerText.slice(0,500)'));throw error;}
  assert.equal(profiles[0].id,profiles[1].id);assert.equal(profiles[0].isSandbox,true);assert.equal(profiles[0].username,'ankt_sandbox_test');
  await wait(()=>evaluate(a.sessionId,"document.querySelector('[role=status]')?.dataset.state==='Connected'"));
  console.log('PASS two real Developer logins -> iframe handshake -> code/PKCE exchange -> shared database UserInfo; no OIDC or cross-site login cookie');
  await wait(()=>evaluate(a.sessionId,`!!document.querySelector('a[download="ankt-miniapp.js"]')`));
  const download=await evaluate(a.sessionId,`(async()=>{const response=await fetch(document.querySelector('a[download="ankt-miniapp.js"]').href);const bytes=await response.arrayBuffer();return {status:response.status,type:response.headers.get('content-type'),size:bytes.byteLength,hash:[...new Uint8Array(await crypto.subtle.digest('SHA-256',bytes))].map(x=>x.toString(16).padStart(2,'0')).join('')};})()`);
  assert.equal(download.status,200);assert.match(download.type,/javascript/);assert.equal(download.size,sdkManifest.bytes);assert.equal(download.hash,sdkManifest.sha256);
  console.log('PASS Developer Portal download returns the same standalone SDK bytes used by the Mini App');
  await evaluate(a.sessionId,"[...document.querySelectorAll('button')].find(b=>b.textContent==='Mở lại Sandbox').click();true");assert.equal((await profile(a)).id,profiles[0].id);console.log('PASS close/reopen Sandbox with fresh bridge session');
  const production=await tab('/production?app='+config.accounts[0].appId);const real=await profile(production);assert.equal(real.isSandbox,false);assert.equal(real.displayName,'ANKT Real User');assert.notEqual(real.id,profiles[0].id);
  await wait(()=>evaluate(production.sessionId,"document.querySelector('#production-status')?.textContent==='bridge.connected'"));console.log('PASS ANKT Web actual host adapter uses existing signed user login and separate Production Mini App token');
  const beforeRefresh=partnerSessions.size;
  await cdp.send('Page.reload',{},production.sessionId);
  await profile(production);
  await wait(()=>partnerSessions.size>beforeRefresh);
  assert.ok(partnerAccounts.size>=3); // Two Sandbox app namespaces and one Production account.
  console.log('PASS browser refresh -> fresh login code -> partner account reuse and separate partner session');
  assert.ok(exchanges>=4);
  const local=await login(config.accounts[0],localOrigin);const localUser=await profile(local);
  assert.equal(localUser.isSandbox,true);assert.equal(localUser.id,profiles[0].id);
  await wait(()=>evaluate(local.sessionId,"document.querySelector('[role=status]')?.dataset.state==='Connected'"));
  const sample=await evaluate(local.sessionId,"[...document.querySelectorAll('pre code')].map(x=>x.textContent).join('\\n')");
  assert.ok(sample.includes('hostOrigin: "http://localhost:5495"'));
  console.log('PASS HTTP localhost Portal -> existing HTTPS backend -> real Sandbox UserInfo; snippet pins exact local host');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:375,height:667,deviceScaleFactor:1,mobile:true},a.sessionId);
  const shot=await cdp.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},a.sessionId);await writeFile(resolve(import.meta.dirname,'../../.codex-tmp/auth-bridge-browser.png'),Buffer.from(shot.data,'base64'));
  }
  for(const context of contexts)await cdp.send('Target.disposeBrowserContext',{browserContextId:context});
}finally{await browser?.close();portal.closeAllConnections();localPortal.closeAllConnections();partner.closeAllConnections();await Promise.all([new Promise(r=>portal.close(r)),new Promise(r=>localPortal.close(r)),new Promise(r=>partner.close(r))]);}
