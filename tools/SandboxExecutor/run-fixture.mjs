import {spawn} from 'node:child_process';
import {join,resolve} from 'node:path';
import {readFile} from 'node:fs/promises';
import https from 'node:https';
import {browserEnvironment} from './cdp.mjs';
const root=resolve(import.meta.dirname,'../..');
const issuer=spawn('C:/Program Files/dotnet/dotnet.exe',['tools/SsoE2EHost/bin/Debug/net8.0/SsoE2EHost.dll','--sandbox-executor-fixture',...process.argv.slice(2)],{cwd:root,stdio:['ignore','pipe','pipe'],windowsHide:true,env:browserEnvironment()});
const exit=new Promise(resolve=>issuer.once('exit',resolve));let buffer='',started=false;
const timer=setTimeout(()=>issuer.kill(),180000);
issuer.stdout.setEncoding('utf8');issuer.stdout.on('data',chunk=>{
  buffer+=chunk;const directory=buffer.match(/Partner fixture directory: ([^\r\n]+)[\r\n]/)?.[1];
  if(!directory||started)return;started=true;
  const child=spawn(process.execPath,[join(import.meta.dirname,process.argv.includes('--portal-e2e')?'portal-e2e.mjs':'fixture.mjs'),directory],{cwd:root,stdio:'inherit',windowsHide:true,env:{...browserEnvironment(),NODE_EXTRA_CA_CERTS:join(directory,'root-ca.pem')}});
  child.once('exit',async code=>{
    if(code!==0)process.exitCode=code??1;
    // Even an unexpected child crash must gracefully stop the disposable issuer.
    try{
      const config=JSON.parse(await readFile(join(directory,'fixture.json'),'utf8'));
      const ca=await readFile(join(directory,'root-ca.pem'));
      await new Promise((resolve,reject)=>{
        const request=https.request('https://127.0.0.1:5490/fixture/executor-shutdown',{method:'POST',ca,
          headers:{Host:new URL(config.issuer).host,Authorization:'SandboxExecutor '+config.executorKey}},response=>{response.resume();response.once('end',resolve);});
        request.on('error',reject);request.setTimeout(3000,()=>request.destroy());request.end();
      });
    }catch{/* Normal child cleanup already removed its fixture. */}
  });
});
let failure='';issuer.stderr.setEncoding('utf8');issuer.stderr.on('data',chunk=>{if(failure.length<300)failure+=chunk;});
const code=await exit;clearTimeout(timer);if(code!==0||!started){console.error('Disposable fixture failed: '+failure.split(/\r?\n/)[0].slice(0,200));process.exitCode=1;}
