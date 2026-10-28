import https from 'node:https';
import {readFile} from 'node:fs/promises';
import {join,resolve} from 'node:path';
import {executorClient,executeJob} from './executor.mjs';
import {pathToFileURL} from 'node:url';
import assert from 'node:assert/strict';

// Own ephemeral fixture material only. Never reads a user browser profile or app secrets.
export async function startPartner(directory){
const sdk=resolve(import.meta.dirname,'../../../viora/packages/miniapp-sdk/dist');
const script=`import {connectHostBridge} from '/bridge.js';
  try {const bridge=await connectHostBridge('https://sandbox.ankt.localhost:5493',{protocolVersion:2});
    const context=await bridge.getContext();document.querySelector('p').textContent=context.environment;
  }catch{document.querySelector('p').textContent='Bridge unavailable';}`;
const server=https.createServer({cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))},async(req,res)=>{
  res.setHeader('Cache-Control','no-store');res.setHeader('Referrer-Policy','no-referrer');
  if(req.url==='/bridge.js'||req.url==='/config.js'){
    res.setHeader('Content-Type','text/javascript');res.end(await readFile(join(sdk,req.url.slice(1))));return;
  }
  if(req.url==='/fixture.js'){res.setHeader('Content-Type','text/javascript');res.end(script);return;}
  if(req.url==='/delayed.js'){res.setHeader('Content-Type','text/javascript');res.end("setTimeout(()=>import('/fixture.js'),45000);");return;}
  if(req.url==='/redirect'){res.writeHead(302,{Location:'https://127.0.0.1:5490/'});res.end();return;}
  res.setHeader('Content-Security-Policy',"default-src 'none'; script-src 'self'; frame-ancestors "+(req.url==='/blocked'?"'none'":'https://sandbox.ankt.localhost:5493 https://portal.ankt.localhost:5494'));
  res.setHeader('Content-Type','text/html; charset=utf-8');res.end('<!doctype html><html lang=en><meta charset=utf-8><title>Controlled SDK partner</title><body><h1>Controlled SDK partner</h1><p>Connecting</p><script type=module src='+ (req.url==='/delayed'?'/delayed.js':'/fixture.js')+'></script></body></html>');
});
await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(443,'127.0.0.1',resolve);});
return {close:()=>new Promise(resolve=>server.close(resolve))};
}
async function main(){
const directory=process.argv[2];if(!directory)throw new Error('Disposable fixture directory required');
const config=JSON.parse(await readFile(join(directory,'fixture.json'),'utf8'));
const server=await startPartner(directory);
try{
  const request=executorClient(config.issuer,config.executorKey);let jobs=[];
  for(let attempt=0;attempt<20&&!jobs.length;attempt++){jobs=await request('runs');if(!jobs.length)await new Promise(r=>setTimeout(r,250));}
  if(jobs.length!==1)throw new Error('Expected one live fixture run');
  const observed=await executeJob(jobs[0],{chrome:'C:/Program Files/Google/Chrome/Application/chrome.exe',request,fixtureDirectory:directory,observe:console.log});
  assert.equal(observed.status,config.partnerFailure?'Failed':'Passed');console.log(JSON.stringify(observed));
  await new Promise(r=>setTimeout(r,1000));
  const response=await fetch(new URL('/fixture/executor-result',config.issuer)),results=await response.json();
  assert.equal(results.length,2);assert.ok(results.every(x=>x.status===observed.status&&x.source==='TrustedBrowser'));console.log(JSON.stringify(results));
}finally{
  await server.close();
  await fetch(new URL('/fixture/executor-shutdown',config.issuer),{method:'POST',headers:{Authorization:'SandboxExecutor '+config.executorKey}}).catch(()=>{});
}
}
if(process.argv[1]&&import.meta.url===pathToFileURL(process.argv[1]).href)await main();
