import https from 'node:https';
import {readFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {createRequire} from 'node:module';
import {pathToFileURL} from 'node:url';
import {fixtureFetch} from './fixture-fetch.mjs';
const frontend=resolve(import.meta.dirname,'../../../viora'),sdk=join(frontend,'packages/miniapp-sdk');
const require=createRequire(join(sdk,'package.json'));
const {build}=require('esbuild');
const oidc=await import(pathToFileURL(require.resolve('openid-client')).href);
const {PrivateStore,readCookie,cookie}=await import(pathToFileURL(join(sdk,'examples/confidential/session.mjs')).href);

export async function startSsoPartner(directory,config,extra){
  const origin='https://partner.ankt.example.com',redirectUri=origin+'/callback';
  const bundle=await build({entryPoints:[join(import.meta.dirname,'partner-auth.fixture.ts')],bundle:true,write:false,format:'esm',target:'es2022',platform:'browser',external:['/embedded.js']});
  const productionBundle=config.productionFixture?await build({entryPoints:[join(import.meta.dirname,'../MiniAppProductionE2E/partner.fixture.ts')],bundle:true,write:false,format:'esm',target:'es2022',platform:'browser'}):null;
  const transactions=new PrivateStore(),sessions=new PrivateStore();
  let completedAuthorizations=0,logoRequests=0;
  const client=config.partnerClientType==='Confidential'?await oidc.discovery(new URL(config.issuer),config.partnerClientId,
    {token_endpoint_auth_method:'client_secret_basic',id_token_signed_response_alg:'RS256'},oidc.ClientSecretBasic(config.confidentialSecret),{
      execute:[oidc.enableNonRepudiationChecks],[oidc.customFetch]:(url,options)=>{if(new URL(url).origin!==new URL(config.issuer).origin)throw Error();return fixtureFetch(url,{...options,redirect:'error',credentials:'omit'});}}):null;
  const embedded=await readFile(join(sdk,'examples/confidential/embedded.mjs'),'utf8'),completion=await readFile(join(sdk,'examples/confidential/popup-complete.mjs'),'utf8');
  const activeRun=async()=>{const response=await fixtureFetch(new URL('/fixture/active-run',config.issuer),{headers:{Authorization:'SandboxExecutor '+config.executorKey},signal:AbortSignal.timeout(5000)});if(!response.ok)throw Error();return (await response.json()).runId;};
  const server=https.createServer({cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))},async(req,res)=>{
    if(req.headers.host==='identity.ankt.example.com'){
      const upstream=https.request(new URL(req.url,'https://127.0.0.1:5490'),{method:req.method,headers:req.headers,ca:await readFile(join(directory,'root-ca.pem'))},response=>{
        res.writeHead(response.statusCode,response.headers);response.pipe(res);
      });upstream.on('error',()=>{res.writeHead(502);res.end();});req.pipe(upstream);return;
    }
    res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
    res.setHeader('Content-Security-Policy',"default-src 'none'; script-src 'self'; connect-src 'self' "+new URL(config.issuer).origin+"; img-src 'self'; frame-ancestors "+config.sandboxHostOrigin+' '+config.sandboxPortalOrigin+(productionBundle?' '+config.productionHostOrigin+' '+config.productionAppOrigin:''));
    const url=new URL(req.url,origin),html=body=>{res.setHeader('Content-Type','text/html; charset=utf-8');res.end('<!doctype html><html lang=en><meta charset=utf-8><title>Controlled real SSO partner</title><body>'+body+'</body></html>');};
    const redirect=target=>{res.writeHead(302,{Location:target});res.end();};
    try{
      if(extra&&url.pathname.startsWith('/production/')&&await extra.handle(req,res,url))return;
      if(req.method!=='GET'||req.headers.host!==new URL(origin).host)throw Error();
      if(url.pathname==='/config.json'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({issuer:config.issuer,clientId:config.partnerClientId,clientType:config.partnerClientType,redirectUri,hostOrigin:config.sandboxHostOrigin,runId:await activeRun()}));return;}
      if(productionBundle&&url.pathname==='/production-host-config.json'){res.setHeader('Content-Type','application/json');res.end(JSON.stringify({hostOrigin:config.productionHostOrigin}));return;}
      if(productionBundle&&(url.pathname==='/production-host.js'||url.pathname==='/production/embedded-callback.js')){res.setHeader('Content-Type','text/javascript');res.end(productionBundle.outputFiles[0].contents);return;}
      if(url.pathname==='/sandbox-app.js'||url.pathname==='/app.js'||url.pathname==='/embedded.js'||url.pathname==='/popup-complete.js'){
        res.setHeader('Content-Type','text/javascript');
        if(productionBundle&&url.pathname==='/app.js')res.end(`import(location.ancestorOrigins?.[0]===${JSON.stringify(config.productionHostOrigin)}?'/production-host.js':'/sandbox-app.js');`);
        else res.end(url.pathname==='/app.js'||url.pathname==='/sandbox-app.js'?bundle.outputFiles[0].contents:url.pathname==='/embedded.js'?embedded:completion);return;
      }
      if(url.pathname==='/logo.svg'){logoRequests++;res.setHeader('Content-Type','image/svg+xml');res.end('<svg xmlns="http://www.w3.org/2000/svg" width="64" height="64"><rect width="64" height="64" fill="#117a99"/></svg>');return;}
      if(url.pathname==='/help'){html('<h1>Controlled external destination</h1>');return;}
      if(url.pathname==='/privacy'||url.pathname==='/terms'){html('<h1>Controlled partner legal page</h1>');return;}
      if(client&&url.pathname==='/login'){
        const attempt=url.searchParams.get('popup');if(!/^[0-9a-f-]{36}$/.test(attempt??''))throw Error();
        const verifier=oidc.randomPKCECodeVerifier(),state=oidc.randomState(),nonce=oidc.randomNonce(),runId=await activeRun();
        const key=transactions.put({verifier,state,nonce,attempt},300000);res.setHeader('Set-Cookie',cookie('__Host-FixtureLogin',key,300));
        const authorization=oidc.buildAuthorizationUrl(client,{redirect_uri:redirectUri,scope:'openid profile email',state,nonce,code_challenge_method:'S256',code_challenge:await oidc.calculatePKCECodeChallenge(verifier),...(runId?{sandbox_run:runId}:{})});redirect(authorization.href);return;
      }
      if(client&&url.pathname==='/callback'){
        const key=readCookie(req.headers.cookie,'__Host-FixtureLogin'),transaction=transactions.claim(key);if(!transaction)throw Error();
        const tokens=await oidc.authorizationCodeGrant(client,url,{pkceCodeVerifier:transaction.verifier,expectedState:transaction.state,expectedNonce:transaction.nonce,idTokenExpected:true});
        const claims=tokens.claims();if(!claims?.sub||!Number.isFinite(tokens.expires_in)||tokens.expires_in<=0||tokens.expires_in>300||tokens.refresh_token)throw Error();
        await oidc.fetchUserInfo(client,tokens.access_token,claims.sub);if(transactions.take(key)!==transaction)throw Error();completedAuthorizations++;
        const session=sessions.put({token:tokens.access_token,sub:claims.sub,expires:Date.now()+tokens.expires_in*1000},900000);
        res.setHeader('Set-Cookie',[cookie('__Host-FixtureLogin','',0),config.restrictPartnerCookie?cookie('__Host-FixturePartner',session,900):cookie('__Host-FixturePartner',session,900).replace('SameSite=Lax','SameSite=None')]);
        html('<h1>Confidential completion</h1><p id=status>revalidate_embedded_session</p><meta name=popup-attempt content="'+transaction.attempt+'"><script src=/popup-complete.js></script>');return;
      }
      const session=client?sessions.get(readCookie(req.headers.cookie,'__Host-FixturePartner')):null;
      if(client&&url.pathname==='/session'){
        if(!session||session.expires<=Date.now())throw Error();await oidc.fetchUserInfo(client,session.token,session.sub);
        res.setHeader('Content-Type','application/json');res.end('{"authenticated":true}');return;
      }
      if(url.pathname==='/'||url.pathname==='/callback'){
        // Shared shell does not authorize either environment. Its selected runtime
        // validates its own session after the bridge identifies Sandbox/Production.
        html('<h1>Controlled '+config.partnerClientType+' partner</h1><p id=context>connecting</p><p id=run-ready>waiting</p><p id=status>unauthenticated</p>'+
          (client?'<button id=popup-login>Login in popup</button>':'<button id=login>Login</button><button id=force>Force consent</button><button id=logout>Local logout</button>')+'<button id=external>External link</button><button id=close>Close Sandbox</button><p id=bridge-result>idle</p><script type=module src=/app.js></script>');return;
      }
      res.writeHead(404);res.end();
    }catch{res.statusCode=400;html('<h1>Partner request failed safely</h1>');}
  });
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(443,'127.0.0.1',resolve);});return {authorizations:()=>completedAuthorizations,logoRequests:()=>logoRequests,close:()=>new Promise(resolve=>server.close(resolve))};
}
