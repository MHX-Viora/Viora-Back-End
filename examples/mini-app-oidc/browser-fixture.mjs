// Local browser fixture: synthetic ANKT host/provider and real partner BFF/SDK.
// Never use production accounts, certificates, credentials or provider endpoints.
import { createServer } from 'node:https';
import { readFileSync } from 'node:fs';
import { generateKeyPairSync, sign, createHash, randomUUID } from 'node:crypto';
import { once } from 'node:events';
import * as client from 'openid-client';
import { createPartnerServer } from './server.mjs';
import { oidcAdapter } from './oidc.mjs';

if (!process.env.ANKT_TEST_CERT_PATH || !process.env.ANKT_TEST_CERT_PASSWORD)
  throw new Error('Set ANKT_TEST_CERT_PATH and ANKT_TEST_CERT_PASSWORD to a localhost test PFX.');
const tls = { pfx: readFileSync(process.env.ANKT_TEST_CERT_PATH), passphrase: process.env.ANKT_TEST_CERT_PASSWORD };
const hostOrigin = 'https://localhost:4311';
const partnerOrigin = 'https://localhost:4312';
const issuer = 'https://localhost:4313';
const redirectUri = `${partnerOrigin}/auth/ankt/callback`;
const sdk = readFileSync(new URL('../../viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js', import.meta.url));
const keys = generateKeyPairSync('rsa', { modulusLength: 2048 });
const jwk = { ...keys.publicKey.export({ format: 'jwk' }), kid: 'browser-synthetic', alg: 'RS256', use: 'sig' };
const encode = value => Buffer.from(JSON.stringify(value)).toString('base64url');
const jwt = claims => {
  const input = `${encode({ alg: 'RS256', kid: jwk.kid })}.${encode(claims)}`;
  return `${input}.${sign('RSA-SHA256', Buffer.from(input), keys.privateKey).toString('base64url')}`;
};
const grants = new Map(), subjects = new Map();
function json(response, status, value) {
  response.writeHead(status, { 'Content-Type': 'application/json', 'Cache-Control': 'no-store' });
  response.end(JSON.stringify(value));
}
function providerResponse(url, options = {}) {
  if (url.pathname === '/oauth/jwks') return Response.json({ keys: [jwk] });
  if (url.pathname === '/v1/userinfo') {
    const subject = subjects.get(new Headers(options.headers).get('authorization')?.replace(/^Bearer /i, ''));
    return subject ? Response.json({ sub: subject, name: `Synthetic ${subject}` }) : Response.json({ error: 'invalid_token' }, { status: 401 });
  }
  if (url.pathname !== '/oauth/token') return Response.json({ error: 'not_found' }, { status: 404 });
  const form = new URLSearchParams(options.body), grant = grants.get(form.get('code'));
  if (grant) grants.delete(form.get('code'));
  if (!grant || grant.expiresAt <= Date.now() || form.get('client_id') !== 'browser-synthetic-client' || form.get('client_secret') !== 'browser-synthetic-backend-secret' || form.get('redirect_uri') !== redirectUri ||
      createHash('sha256').update(form.get('code_verifier') || '').digest('base64url') !== grant.challenge)
    return Response.json({ error: 'invalid_grant' }, { status: 400 });
  const now = Math.floor(Date.now() / 1000), accessToken = `browser-synthetic-token-${randomUUID()}`;
  subjects.set(accessToken, grant.subject);
  return Response.json({ access_token: accessToken, token_type: 'Bearer', expires_in: 300,
    id_token: jwt({ iss: issuer, sub: grant.subject, aud: 'browser-synthetic-client', iat: now, exp: now + 300, nonce: grant.nonce, runtime_session_id: grant.runtime }) });
}
const configuration = new client.Configuration({ issuer, authorization_endpoint: `${issuer}/oauth/authorize`, token_endpoint: `${issuer}/oauth/token`, jwks_uri: `${issuer}/oauth/jwks`, userinfo_endpoint: `${issuer}/v1/userinfo` },
  'browser-synthetic-client', { client_secret: 'browser-synthetic-backend-secret', id_token_signed_response_alg: 'RS256' }, client.ClientSecretPost('browser-synthetic-backend-secret'));
client.enableNonRepudiationChecks(configuration);
// Keep real signature/issuer/audience/nonce/state checks. Only provider transport
// is replaced by an in-process signed synthetic provider for this test fixture.
configuration[client.customFetch] = async (url, options) => {
  const parsed = new URL(url);
  if (parsed.origin !== issuer) throw new Error('Synthetic fixture refuses external provider requests');
  return providerResponse(parsed, options);
};
const partnerBff = createPartnerServer({ origin: partnerOrigin, redirectUri, issuer, hostOrigin, clientId: 'browser-synthetic-client', localDevelopment: false }, oidcAdapter(configuration, redirectUri));
const partner = createServer(tls, (request, response) => partnerBff.emit('request', request, response));

const hostPage = `<!doctype html><html><head><meta charset="utf-8"><title>ANKT synthetic auto-login host</title></head>
<body><h1>Local synthetic ANKT host</h1><p>No production session or identity is used.</p>
<p id="account">Main synthetic account A</p><button id="open">Open Mini App</button> <button id="switch">Switch to account B and open</button><p id="status" role="status">Ready</p>
<script>
const partnerOrigin=${JSON.stringify(partnerOrigin)},issuer=${JSON.stringify(issuer)};
let popup=null,runtime=null,nonce=null,account='A';
function openApp(){if(popup&&!popup.closed)popup.close();runtime=crypto.randomUUID();nonce=crypto.randomUUID();popup=window.open(partnerOrigin,'ankt-synthetic-mini-app','popup,width=600,height=760');document.querySelector('#status').textContent='Opened runtime '+runtime;}
document.querySelector('#open').addEventListener('click',openApp);
document.querySelector('#switch').addEventListener('click',()=>{account='B';document.querySelector('#account').textContent='Main synthetic account B';openApp();});
window.addEventListener('message',event=>{
 if(event.origin!==partnerOrigin||event.source!==popup)return;
 const message=event.data;if(!message||typeof message!=='object')return;
 const binding={appId:'browser-synthetic-app',sessionId:runtime,nonce};
 if(message.type==='ANKT_MINI_APP_HELLO'){popup.postMessage({...binding,type:'ANKT_MINI_APP_READY',autoLogin:true},partnerOrigin);return;}
 if(message.type!=='ANKT_MINI_APP_REQUEST'||message.appId!==binding.appId||message.sessionId!==runtime||message.nonce!==nonce)return;
 if(message.method==='reportSsoStatus'){document.querySelector('#status').textContent='Partner status: '+JSON.stringify(message.params);popup.postMessage({...binding,type:'ANKT_MINI_APP_RESPONSE',id:message.id,result:{acknowledged:true}},partnerOrigin);return;}
 if(message.method!=='requestOAuthLogin')return;
 const p=message.params,url=new URL(issuer+'/oauth/authorize');
 for(const [key,value]of Object.entries({client_id:p.clientId,redirect_uri:p.redirectUri,response_type:'code',state:p.state,nonce:p.nonce,code_challenge:p.codeChallenge,code_challenge_method:'S256',scope:p.scopes.join(' '),login_ticket:'public-synthetic-ticket',synthetic_runtime:runtime,synthetic_account:account}))url.searchParams.set(key,value);
 popup.postMessage({...binding,type:'ANKT_MINI_APP_RESPONSE',id:message.id,result:{authorizeUrl:url.href}},partnerOrigin);
 document.querySelector('#status').textContent='Authorized synthetic account '+account;
});
</script></body></html>`;
const host = createServer(tls, (request, response) => {
  if (request.url !== '/') return json(response, 404, { error: 'not_found' });
  response.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8', 'Cache-Control': 'no-store' }); response.end(hostPage);
});
const provider = createServer(tls, async (request, response) => {
  const url = new URL(request.url, issuer);
  if (request.method === 'GET' && url.pathname === '/mini-app-sdk/ankt-mini-app.js') {
    response.writeHead(200, { 'Content-Type': 'text/javascript', 'Cache-Control': 'no-store' }); return response.end(sdk);
  }
  if (request.method === 'GET' && url.pathname === '/oauth/authorize') {
    const p = url.searchParams;
    if (p.get('redirect_uri') !== redirectUri || p.get('client_id') !== 'browser-synthetic-client' || p.get('response_type') !== 'code' || p.get('code_challenge_method') !== 'S256' || !/^[A-Za-z0-9_-]{43}$/.test(p.get('code_challenge') || '') || !['A', 'B'].includes(p.get('synthetic_account')) || !/^[A-Za-z0-9_-]{1,80}$/.test(p.get('synthetic_runtime') || '') || !p.get('state') || !p.get('nonce') || p.get('login_ticket') !== 'public-synthetic-ticket')
      return json(response, 400, { error: 'invalid_synthetic_authorization' });
    const code = randomUUID();
    grants.set(code, { subject: 'pairwise-synthetic-account-' + p.get('synthetic_account'), runtime: p.get('synthetic_runtime'), nonce: p.get('nonce'), challenge: p.get('code_challenge'), expiresAt: Date.now() + 60_000 });
    const callback = new URL(redirectUri); callback.searchParams.set('code', code); callback.searchParams.set('state', p.get('state'));
    response.writeHead(303, { Location: callback.href, 'Cache-Control': 'no-store', 'Referrer-Policy': 'no-referrer' }); return response.end();
  }
  let body = '';for await (const chunk of request) { body += chunk;if (body.length > 8192) return json(response, 413, { error: 'too_large' }); }
  const result = providerResponse(url, { headers: request.headers, body });
  response.writeHead(result.status, Object.fromEntries(result.headers));response.end(await result.text());
});
for (const [server, port] of [[host, 4311], [partner, 4312], [provider, 4313]]) {
  server.listen(port, 'localhost'); await once(server, 'listening');
}
console.log('Synthetic browser fixture ready: https://localhost:4311 (host), :4312 (real partner BFF), :4313 (signed synthetic provider).');
for (const signal of ['SIGINT', 'SIGTERM']) process.on(signal, () => {
  for (const server of [host, partner, provider]) server.close();
});
