const {test}=require('node:test'), assert=require('node:assert/strict'), vm=require('node:vm'), fs=require('node:fs');
const sdk=fs.readFileSync('viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js','utf8');
function load(window){ vm.runInNewContext(sdk,{window,URL,Map,Set,Date,Promise,Object,Error,JSON,setTimeout,clearTimeout});return window.ANKT; }
test('late SDK loading initiates exact-origin HELLO and completes handshake before request',async()=>{
 const listeners={}, sent=[], parent={}; const window={parent,addEventListener:(type,handler)=>listeners[type]=handler};
 const binding={appId:'app-a',sessionId:'session-a',nonce:'nonce-a',type:'ANKT_MINI_APP_READY'};
 parent.postMessage=(data,origin)=>{sent.push({data,origin}); if(data.type==='ANKT_MINI_APP_HELLO')listeners.message({data:binding,source:parent,origin});else if(data.type==='ANKT_MINI_APP_REQUEST') listeners.message({data:{...binding,type:'ANKT_MINI_APP_RESPONSE',id:data.id,result:{name:'App'}},source:parent,origin});};
 const ankt=load(window); await ankt.initialize({hostOrigin:'https://host.example'}); assert.equal(ankt.getEnvironment().embedded,true); assert.equal(ankt.getEnvironment().transport,'web');
 assert.equal((await ankt.getAppInfo()).name,'App'); assert.equal(sent[0].data.type,'ANKT_MINI_APP_HELLO');assert.ok(sent.every(x=>x.origin==='https://host.example'));
 assert.doesNotMatch(JSON.stringify(sent),/accessToken|sessionToken|clientSecret/);
});
test('BFF login/profile/logout remain same-origin, CSRF bound, and never fetch internal APIs',async()=>{
 const calls=[],navigations=[];const params={clientId:'client-a',redirectUri:'https://partner.example/auth/ankt/callback',state:'s'.repeat(1200),nonce:'638965863321.abcdefghijklmnop+/=',codeChallenge:'a'.repeat(43),codeChallengeMethod:'S256',scopes:['openid','profile']};
 const url=new URL('https://openapi.example/oauth/authorize');for(const [key,value]of Object.entries({client_id:params.clientId,redirect_uri:params.redirectUri,state:params.state,nonce:params.nonce,code_challenge:params.codeChallenge,code_challenge_method:'S256',response_type:'code',scope:'openid profile'}))url.searchParams.set(key,value);
 const window={addEventListener:()=>{},location:{origin:'https://partner.example',assign:value=>navigations.push(value)},fetch:async(path,options)=>{calls.push({path,options});return {ok:true,json:async()=>path.endsWith('session')?{csrfToken:'csrf-abcdefghijklmnop',issuer:'https://openapi.example'}:path.endsWith('start')?{sdkParams:params,authorizeUrl:url.href}:path.endsWith('profile')?{sub:'pairwise-sub'}:{loggedOut:true}};}};window.parent=window;
 const ankt=load(window);ankt.configure({hostOrigin:'https://host.example',bff:true});await ankt.auth.login();assert.equal(navigations[0],url.href);assert.equal((await ankt.user.getBasicProfile()).sub,'pairwise-sub');assert.equal((await ankt.auth.getSession()).csrfToken,'csrf-abcdefghijklmnop');assert.equal((await ankt.auth.logout()).loggedOut,true);
 assert.ok(calls.every(x=>x.path.startsWith('/auth/ankt/')&&x.options.credentials==='same-origin'&&x.options.redirect==='error'));
 assert.ok(calls.filter(x=>x.options.method==='POST').every(x=>x.options.headers['X-CSRF-Token']==='csrf-abcdefghijklmnop'));
 assert.doesNotMatch(JSON.stringify(calls),/access_token|id_token|client_secret|code_verifier/);
 assert.throws(()=>load(window).configure({hostOrigin:'https://host.example',bff:{startPath:'https://evil.example'}}),{code:'INVALID_BFF'});
});
test('BFF-less OAuth helpers fail closed and invalid scopes never enter bridge',async()=>{
 const window={addEventListener:()=>{},parent:{postMessage:()=>{throw new Error('must not send');}}};const ankt=load(window);
 await assert.rejects(ankt.auth.login(),{code:'BFF_REQUIRED'});await assert.rejects(ankt.user.getProfile(),{code:'BFF_REQUIRED'});
 await assert.rejects(ankt.auth.login({redirectUri:'https://partner.example',state:'s'.repeat(32),nonce:'n'.repeat(32),codeChallenge:'a'.repeat(43),codeChallengeMethod:'S256',scopes:['openid','wallet']}),{code:'INVALID_REQUEST'});
});
test('native injected binding can precede BFF configuration; web host origin stays immutable',()=>{
 const native={parent:{},ReactNativeWebView:{postMessage:()=>{}},location:{origin:'https://partner.example'},addEventListener:()=>{}};
 const sdk=load(native);native.__ANKT_NATIVE_READY__({type:'ANKT_MINI_APP_READY',appId:'app',sessionId:'session',nonce:'nonce'});
 assert.doesNotThrow(()=>sdk.configure({hostOrigin:'https://host.example',bff:true}));
 const listeners={},parent={postMessage:()=>{}};const web={parent,addEventListener:(name,fn)=>listeners[name]=fn};const webSdk=load(web);
 webSdk.configure({hostOrigin:'https://host.example'});listeners.message({source:parent,origin:'https://host.example',data:{type:'ANKT_MINI_APP_READY',appId:'app',sessionId:'session',nonce:'nonce'}});
 assert.throws(()=>webSdk.configure({hostOrigin:'https://evil.example'}),{code:'ALREADY_READY'});
});

function autoLoginFixture({autoLogin=true,partnerRuntimeSessionId=null,authenticated=false}={}){
 const listeners={},calls=[],sent=[],navigations=[],parent={};
 const binding={type:'ANKT_MINI_APP_READY',appId:'app-a',sessionId:'runtime-a',nonce:'binding-nonce-a',autoLogin};
 const params={clientId:'client-a',redirectUri:'https://partner.example/auth/ankt/callback',state:'state-abcdefghijklmnop',nonce:'nonce-abcdefghijklmnop',codeChallenge:'a'.repeat(43),codeChallengeMethod:'S256',scopes:['openid','profile']};
 const authorization=new URL('https://openapi.example/oauth/authorize');
 for(const [key,value]of Object.entries({client_id:params.clientId,redirect_uri:params.redirectUri,state:params.state,nonce:params.nonce,code_challenge:params.codeChallenge,code_challenge_method:'S256',response_type:'code',scope:'openid profile'}))authorization.searchParams.set(key,value);
 const window={parent,addEventListener:(type,handler)=>listeners[type]=handler,location:{origin:'https://partner.example',assign:value=>navigations.push(value)},fetch:async(path,options)=>{
  calls.push({path,options});return {ok:true,json:async()=>path.endsWith('session')?{authenticated,runtimeSessionId:partnerRuntimeSessionId,csrfToken:'csrf-abcdefghijklmnop',issuer:'https://openapi.example'}:{sdkParams:params,authorizeUrl:authorization.href}};
 }};
 parent.postMessage=(data,origin)=>{
  sent.push({data,origin});
  if(data.type==='ANKT_MINI_APP_HELLO')listeners.message({data:binding,source:parent,origin});
  else if(data.type==='ANKT_MINI_APP_REQUEST')listeners.message({data:{...binding,type:'ANKT_MINI_APP_RESPONSE',id:data.id,result:{authorizeUrl:authorization.href}},source:parent,origin});
 };
 const ankt=load(window);ankt.configure({hostOrigin:'https://host.example',bff:true});
 return {ankt,calls,sent,navigations,authorization};
}

test('embedded auto login uses the current host session and the partner-owned PKCE transaction',async()=>{
 const fixture=autoLoginFixture();await fixture.ankt.auth.autoLogin();
 assert.equal(fixture.navigations[0],fixture.authorization.href);
 const start=fixture.calls.filter(call=>call.path==='/auth/ankt/start');assert.equal(start.length,1);
 assert.deepEqual(JSON.parse(start[0].options.body),{appId:'app-a',runtimeSessionId:'runtime-a'});
 assert.equal(start[0].options.headers['X-CSRF-Token'],'csrf-abcdefghijklmnop');
 assert.equal(fixture.sent.filter(item=>item.data.method==='requestOAuthLogin').length,1);
 assert.doesNotMatch(JSON.stringify(fixture.calls),/access_token|id_token|client_secret|code_verifier/);
});

test('auto login reuses an authenticated partner session bound to this runtime only',async()=>{
 const fixture=autoLoginFixture({authenticated:true,partnerRuntimeSessionId:'runtime-a'});await fixture.ankt.auth.autoLogin();
 assert.equal(fixture.calls.filter(call=>call.path==='/auth/ankt/session').length,1);
 assert.equal(fixture.calls.filter(call=>call.path==='/auth/ankt/start').length,0);
 assert.equal(fixture.sent.filter(item=>item.data.method==='requestOAuthLogin').length,0);
 assert.equal(fixture.navigations.length,0);
});

test('a partner session from another runtime must reauthenticate before auto login succeeds',async()=>{
 const fixture=autoLoginFixture({authenticated:true,partnerRuntimeSessionId:'runtime-other-account'});await fixture.ankt.auth.autoLogin();
 assert.equal(fixture.calls.filter(call=>call.path==='/auth/ankt/start').length,1);
 assert.equal(fixture.sent.filter(item=>item.data.method==='requestOAuthLogin').length,1);
 assert.equal(fixture.navigations[0],fixture.authorization.href);
});

test('Independent host configuration disables automatic identity requests even with a stale partner session',async()=>{
 const fixture=autoLoginFixture({autoLogin:false,authenticated:true,partnerRuntimeSessionId:'runtime-other-account'});await fixture.ankt.auth.autoLogin();
 assert.equal(fixture.calls.length,0);
 assert.equal(fixture.sent.filter(item=>item.data.method==='requestOAuthLogin').length,0);
 assert.equal(fixture.navigations.length,0);
});
