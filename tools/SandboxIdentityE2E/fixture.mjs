import assert from 'node:assert/strict';
import https from 'node:https';
import {readFile,writeFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {pathToFileURL} from 'node:url';
import {createRequire} from 'node:module';
import {setTimeout as delay} from 'node:timers/promises';
import {createHash,X509Certificate} from 'node:crypto';
import {launchChrome} from '../SandboxExecutor/cdp.mjs';
const directory=process.argv[2],config=JSON.parse(await readFile(join(directory,'identity-fixture.json'),'utf8'));
const frontend=resolve(import.meta.dirname,'../../../viora'),require=createRequire(join(frontend,'package.json'));
const {build}=require('esbuild'),{createDeveloperHandler}=require(join(frontend,'server/developer/handler.cjs'));
const {configureSandboxPolicy}=require(join(frontend,'server/developer/sandbox-policy.cjs'));
const base=require(join(frontend,'server/developer/vercel-base.json'));
const sdkRequire=createRequire(join(frontend,'packages/miniapp-sdk/package.json'));
const oidc=await import(pathToFileURL(sdkRequire.resolve('openid-client')).href);
const tls={cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))};
const ca=await readFile(join(directory,'root-ca.pem'));
function localFetch(input,init={}){
  const url=new URL(String(input));assert.equal(url.origin,new URL(config.issuer).origin);
  return new Promise((resolve,reject)=>{
    const body=init.body instanceof URLSearchParams?init.body.toString():init.body;
    const headers=Object.fromEntries(new Headers(init.headers));headers.Host=url.host;
    if(body!=null)headers['Content-Length']=Buffer.byteLength(body);
    const request=https.request({hostname:'127.0.0.1',port:5490,path:url.pathname+url.search,method:init.method??'GET',headers,ca,servername:url.hostname,signal:init.signal},response=>{
      const chunks=[];response.on('data',chunk=>chunks.push(chunk));response.on('end',()=>{
        const headers=new Headers();for(let i=0;i<response.rawHeaders.length;i+=2)headers.append(response.rawHeaders[i],response.rawHeaders[i+1]);
        if(init.redirect==='error'&&response.statusCode>=300&&response.statusCode<400){reject(Error('Unexpected issuer redirect'));return;}
        const bytes=Buffer.concat(chunks);
        if(response.statusCode>=400&&!(response.statusCode===404&&url.pathname.endsWith('/sandbox-test'))){let code='unknown';try{const data=JSON.parse(bytes);code=typeof data.error==='string'?data.error:data.error?.code??'unknown';}catch{}console.error('Issuer rejected:',url.pathname,response.statusCode,String(code).replace(/[^A-Za-z0-9_]/g,''));}
        resolve(new Response([204,304].includes(response.statusCode)?null:bytes,{status:response.statusCode,headers}));
      });
    });request.on('error',reject);request.setTimeout(15000,()=>request.destroy(Error('Issuer timeout')));request.end(body);
  });
}
const clients=new Map();
for(const account of config.accounts)clients.set(account.key,await oidc.discovery(new URL(config.issuer),account.clientId,
  {token_endpoint_auth_method:'client_secret_basic',id_token_signed_response_alg:'RS256'},oidc.ClientSecretBasic(account.clientSecret),
  {execute:[oidc.enableNonRepudiationChecks],[oidc.customFetch]:localFetch}));
const bundle=await build({entryPoints:[join(import.meta.dirname,'portal.fixture.jsx')],nodePaths:[join(frontend,'node_modules')],alias:{react:join(frontend,'node_modules/react'),'react-dom':join(frontend,'node_modules/react-dom')},bundle:true,write:false,outfile:'fixture.js',jsx:'automatic',define:{'process.env.NODE_ENV':'"development"'}});
const assets=new Map(bundle.outputFiles.map(file=>[file.path.endsWith('.css')?'/fixture.css':'/fixture.js',file.contents]));
const bff=createDeveloperHandler({siteOrigin:config.portal,apiOrigin:config.issuer,production:true,bffSecret:config.bffKey,clientIp:'127.0.0.1'},localFetch);
const policies=configureSandboxPolicy(base,{EXPO_PUBLIC_OPEN_API_ISSUER:config.issuer}).headers.filter(x=>x.source.startsWith('/developers/')).map(x=>({...x,match:new RegExp('^'+x.source.replace(':path*','.*')+'$')}));
const portal=https.createServer(tls,async(req,res)=>{
  try{const url=new URL(req.url,config.portal);for(const rule of policies)if(rule.match.test(url.pathname))for(const h of rule.headers)res.setHeader(h.key,h.value);
    if(url.pathname.startsWith('/developers/api/')){await bff(req,res);return;}
    if(assets.has(url.pathname)){res.setHeader('Content-Type',url.pathname.endsWith('.css')?'text/css':'text/javascript');res.end(assets.get(url.pathname));return;}
    res.setHeader('Content-Type','text/html; charset=utf-8');res.end('<!doctype html><html lang=vi><meta charset=utf-8><meta name=viewport content="width=device-width,initial-scale=1"><link rel=icon href="data:,"><link rel=stylesheet href=/fixture.css><div id=root></div><script src=/fixture.js></script></html>');
  }catch{res.writeHead(500);res.end('Fixture portal failed');}
});
const transactions=new Map(),sessions=new Map(),partnerAccounts=new Map();
const cookie=(req,name)=>String(req.headers.cookie??'').split(';').map(x=>x.trim()).find(x=>x.startsWith(name+'='))?.slice(name.length+1);
const escape=value=>String(value).replaceAll('&','&amp;').replaceAll('<','&lt;').replaceAll('"','&quot;');
const partner=https.createServer(tls,async(req,res)=>{
  const url=new URL(req.url,config.partner),key=url.pathname.split('/')[1],account=config.accounts.find(a=>a.key===key),client=clients.get(key);
  const redirect=target=>{res.writeHead(302,{Location:target,'Cache-Control':'no-store'});res.end();};
  try{
    if(url.pathname==='/favicon.ico'){res.writeHead(204);res.end();return;}
    if(!account||req.method!=='GET')throw Error('Partner route');
    const redirectUri=config.partner+'/'+key+'/callback',cookieName='__Host-SandboxPartner-'+key;
    if(url.pathname==='/'+key){
      const verifier=oidc.randomPKCECodeVerifier(),state=oidc.randomState(),nonce=oidc.randomNonce(),id=crypto.randomUUID();
      transactions.set(id,{key,verifier,state,nonce,expires:Date.now()+60000});
      res.setHeader('Set-Cookie',cookieName+'Login='+id+'; Path=/; Secure; HttpOnly; SameSite=Lax');
      redirect(oidc.buildAuthorizationUrl(client,{redirect_uri:redirectUri,scope:'openid profile email',state,nonce,code_challenge_method:'S256',code_challenge:await oidc.calculatePKCECodeChallenge(verifier)}).href);return;
    }
    if(url.pathname==='/'+key+'/callback'){
      const id=cookie(req,cookieName+'Login'),tx=transactions.get(id);transactions.delete(id);
      assert.ok(tx&&tx.key===key&&tx.expires>Date.now());
      const tokens=await oidc.authorizationCodeGrant(client,url,{pkceCodeVerifier:tx.verifier,expectedState:tx.state,expectedNonce:tx.nonce,idTokenExpected:true});
      assert.equal(tokens.claims().aud,account.clientId);assert.equal(tokens.claims().iss,config.issuer);assert.equal(tokens.claims().nonce,tx.nonce);
      const profile=await oidc.fetchUserInfo(client,tokens.access_token,tokens.claims().sub);
      const identityKey=config.issuer+'\n'+account.clientId+'\n'+profile.sub;
      if(!partnerAccounts.has(identityKey))partnerAccounts.set(identityKey,crypto.randomUUID());
      const session=crypto.randomUUID();sessions.set(session,{profile,partnerAccount:partnerAccounts.get(identityKey)});
      res.setHeader('Set-Cookie',cookieName+'='+session+'; Path=/; Secure; HttpOnly; SameSite=Lax');redirect('/'+key+'/home');return;
    }
    if(url.pathname==='/'+key+'/home'){
      const session=sessions.get(cookie(req,cookieName));assert.ok(session);
      res.setHeader('Content-Type','text/html; charset=utf-8');res.setHeader('Cache-Control','no-store');
      res.end('<!doctype html><html><meta charset=utf-8><h1 id=authenticated>Mini App đã đăng nhập</h1><p id=partner-account>'+escape(session.partnerAccount)+'</p><pre id=profile>'+escape(JSON.stringify(session.profile))+'</pre></html>');return;
    }
    throw Error('Partner route');
  }catch(error){console.error('Partner OAuth:',String(error.message).replace(/[A-Za-z0-9_-]{32,}/g,'[redacted]').slice(0,200));res.writeHead(500);res.end('Partner SSO failed: '+escape(error.code??error.name));}
});
await Promise.all([new Promise(r=>portal.listen(5494,'127.0.0.1',r)),new Promise(r=>partner.listen(443,'127.0.0.1',r))]);
let browser;
try{
  const spki=createHash('sha256').update(new X509Certificate(tls.cert).publicKey.export({type:'spki',format:'der'})).digest('base64');
  browser=await launchChrome('C:/Program Files/Google/Chrome/Application/chrome.exe',['--ignore-certificate-errors-spki-list='+spki,'--no-proxy-server','--host-resolver-rules=MAP portal.ankt.localhost 127.0.0.1, MAP identity.ankt.example.com 127.0.0.1, MAP partner.ankt.example.com 127.0.0.1']);
  const cdp=browser.client;
  cdp.on(event=>{
    if(event.method==='Runtime.exceptionThrown') console.error('Browser script error:',(event.params.exceptionDetails.exception?.description??event.params.exceptionDetails.text).split('\n')[0]);
    if(event.method==='Network.loadingFailed') console.error('Browser resource error:',event.params.errorText);
    if(event.method==='Log.entryAdded'&&event.params.entry.source==='security') console.error('Browser policy:',event.params.entry.text.split('\n')[0]);
  });
  const evaluate=async(sessionId,expression,gesture=false)=>(await cdp.send('Runtime.evaluate',{expression,returnByValue:true,awaitPromise:true,userGesture:gesture},sessionId)).result.value;
  const wait=async action=>{const deadline=Date.now()+30000;while(Date.now()<deadline){try{const value=await action();if(value)return value;}catch{}await delay(150);}throw Error('Browser condition timed out');};
  const attach=async targetId=>{const {sessionId}=await cdp.send('Target.attachToTarget',{targetId,flatten:true});await cdp.send('Runtime.enable',{},sessionId);await cdp.send('Log.enable',{},sessionId);return sessionId;};
  const contexts=[];
  async function login(account){
    const {browserContextId}=await cdp.send('Target.createBrowserContext');contexts.push(browserContextId);
    const {targetId}=await cdp.send('Target.createTarget',{browserContextId,url:'about:blank'});
    const sessionId=await attach(targetId);await cdp.send('Page.enable',{},sessionId);await cdp.send('Network.enable',{},sessionId);
    await cdp.send('Page.navigate',{url:config.portal+'/developers/console/apps/'+account.appId+'/sandbox'},sessionId);
    try{await wait(()=>evaluate(sessionId,"!!document.querySelector('input[name=email]')"));}
    catch(error){console.error('Portal bootstrap:',await evaluate(sessionId,"({path:location.origin+location.pathname,text:document.body?.innerText.slice(0,300)})"));throw error;}
    await evaluate(sessionId,`document.querySelector('[name=email]').value=${JSON.stringify(account.email)};document.querySelector('[name=password]').value=${JSON.stringify(account.password)};document.querySelector('form button').click();true`,true);
    try{await wait(()=>evaluate(sessionId,"[...document.querySelectorAll('button')].some(b=>b.textContent==='Chạy thử Mini App'&&!b.disabled)"));}
    catch(error){console.error('Portal readiness:',await evaluate(sessionId,"document.body?.innerText.slice(0,1000)"));throw error;}
    return {browserContextId,targetId,sessionId};
  }
  let consentCount=0;
  async function run(account,tab){
    const before=(await cdp.send('Target.getTargets')).targetInfos.map(t=>t.targetId);
    await evaluate(tab.sessionId,"[...document.querySelectorAll('button')].find(b=>/^(Chạy thử Mini App|Mở lại Mini App)$/.test(b.textContent)).click();true",true);
    const target=await wait(async()=> (await cdp.send('Target.getTargets')).targetInfos.find(t=>t.browserContextId===tab.browserContextId&&!before.includes(t.targetId)&&t.type==='page'));
    const popup=await attach(target.targetId);await cdp.send('Network.enable',{},popup);let consented=false;
    let profile;
    try{profile=await wait(async()=>{
      const state=await evaluate(popup,"({url:location.href,password:!!document.querySelector('input[type=password]'),consent:!!document.querySelector('button[name=decision][value=allow]'),profile:document.querySelector('#profile')?.textContent,partnerAccount:document.querySelector('#partner-account')?.textContent})");
      if(state?.password||state?.url.includes('/sso/login'))throw Error('Unexpected social login page');
      if(state?.consent&&!consented){consented=true;consentCount++;await evaluate(popup,"document.querySelector('button[name=decision][value=allow]').click();true",true);}
      return state?.profile?{...JSON.parse(state.profile),partnerAccount:state.partnerAccount}:null;
    });}catch(error){console.error('Sandbox popup:',await evaluate(popup,"({path:location.origin+location.pathname,text:document.body?.innerText.slice(0,400)})"));console.error('Portal alerts:',await evaluate(tab.sessionId,"[...document.querySelectorAll('[role=alert]')].map(x=>x.textContent)"));throw error;}
    assert.equal(profile.name,'ANKT Developer Test');assert.equal(profile.nickname,'ankt_sandbox_test');assert.equal(profile.email_verified,false);
    assert.equal(profile.isSandbox,true);assert.equal(profile.username,'ankt_sandbox_test');assert.ok(profile.id);
    assert.equal(profile.birthdate,'2000-01-01');assert.ok(profile.picture.startsWith('https://res.cloudinary.com/'));assert.ok(profile.ankt_cover_picture.startsWith('https://res.cloudinary.com/'));
    await cdp.send('Target.closeTarget',{targetId:target.targetId});return profile;
  }
  const a=await login(config.accounts[0]),b=await login(config.accounts[1]);
  const [first,other]=await Promise.all([run(config.accounts[0],a),run(config.accounts[1],b)]);
  const again=await run(config.accounts[0],a);
  assert.equal(first.sub,again.sub);assert.ok(first.partnerAccount);assert.equal(first.partnerAccount,again.partnerAccount);console.log('PASS Developer A real login, code + PKCE, signed ID token, UserInfo and stable partner account on reopen');
  assert.notEqual(first.sub,other.sub);assert.notEqual(first.partnerAccount,other.partnerAccount);assert.equal(first.id,other.id);
  const cross=await evaluate(b.sessionId,`fetch('/developers/api/account/mini-apps/${config.accounts[0].appId}/sandbox-test',{headers:{'X-ANKT-Developer':'1'}}).then(r=>r.status)`);
  assert.equal(cross,404);assert.equal(consentCount,0);console.log('PASS concurrent Developers A/B: shared database User, separate pairwise subject/session, cross-owner denial and zero consent prompts');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:375,height:667,deviceScaleFactor:1,mobile:true},a.sessionId);
  const size=await evaluate(a.sessionId,'({width:innerWidth,scroll:document.documentElement.scrollWidth})');assert.equal(size.scroll,size.width);
  const height=await evaluate(a.sessionId,'Math.min(document.documentElement.scrollHeight,2400)');
  const screenshot=await cdp.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true,clip:{x:0,y:0,width:375,height,scale:1}},a.sessionId);
  await writeFile(resolve(import.meta.dirname,'../../.codex-tmp/sandbox-identity-mobile.png'),Buffer.from(screenshot.data,'base64'));
  console.log('PASS mobile layout at 375px; Sandbox identity browser E2E completed');
  for(const context of contexts)await cdp.send('Target.disposeBrowserContext',{browserContextId:context});
} finally {
  await browser?.close();await Promise.all([new Promise(r=>portal.close(r)),new Promise(r=>partner.close(r))]);
}
