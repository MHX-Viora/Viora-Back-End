// UI-only browser fixture: backend policy and runtime authorization have separate HTTP tests.
// Uses the disposable SsoE2EHost certificate; never reads application settings.
import {createServer} from 'node:https';
import {readFile} from 'node:fs/promises';
import {join} from 'node:path';
const directory=process.argv[2];
const script=await readFile(new URL('../../viora-BE/Developer/SandboxHost/external-link.mjs',import.meta.url));
const body=`<!doctype html><html lang="en"><meta charset="utf-8"><meta name="viewport" content="width=device-width"><title>External link UI fixture</title>
<style>body{font:16px system-ui;margin:16px}button,a{display:inline-block;padding:12px;margin:4px}dialog{max-width:calc(100vw - 64px)}p{overflow-wrap:anywhere}</style>
<h1>External link UI fixture</h1><p>Controlled UI only; no runtime readiness evidence.</p>
<button id="request">Request link</button><button id="stop">Stop</button><p id="status" role="status">Idle</p><script type="module" src="/fixture.mjs"></script></html>`;
const harness=`import {createExternalLinkOpener} from '/external-link.mjs';
let controller=new AbortController();const status=document.querySelector('#status');
const open=createExternalLinkOpener({document,isStopped:()=>false,request:async()=>({url:'https://localhost:5496/destination',expiresAt:new Date(Date.now()+60000).toISOString()})});
document.querySelector('#request').onclick=()=>{controller=new AbortController();status.textContent='Awaiting confirmation';open('https://localhost:5496/destination',controller.signal).then(()=>status.textContent='Confirmed',()=>status.textContent='Cancelled');};
document.querySelector('#stop').onclick=()=>controller.abort();`;
const server=createServer({cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))},(req,res)=>{
  res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
  if(req.url==='/external-link.mjs'||req.url==='/fixture.mjs'){res.setHeader('Content-Type','text/javascript');res.end(req.url==='/fixture.mjs'?harness:script);}
  else if(req.url==='/destination'){res.setHeader('Content-Type','text/html');res.end('<!doctype html><title>External destination</title><p id="result"></p><script>document.querySelector("#result").textContent="Opener absent: "+(window.opener===null)+"; referrer absent: "+(document.referrer==="");</script>');}
  else if(req.url==='/'){res.setHeader('Content-Type','text/html');res.end(body);}
  else{res.writeHead(404);res.end();}
});
server.listen(5496,'127.0.0.1',()=>console.log('External link UI fixture: https://localhost:5496/'));
