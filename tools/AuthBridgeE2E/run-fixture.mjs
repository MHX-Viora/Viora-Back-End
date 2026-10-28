import {spawn} from 'node:child_process';
import {resolve,join} from 'node:path';
import {browserEnvironment} from '../SandboxExecutor/cdp.mjs';
const root=resolve(import.meta.dirname,'../..');
const host=spawn(join(root,'.codex-tmp/sdk8-check/sdk/dotnet.exe'),[resolve(root,process.argv[2]??'.codex-tmp/auth-bridge-e2e/AuthBridgeE2EHost.dll')],{cwd:root,windowsHide:true,env:browserEnvironment(),stdio:['pipe','pipe','pipe']});
let started=false,output='',child;const done=new Promise(r=>host.once('exit',r));
const timer=setTimeout(()=>{process.exitCode=1;child?.kill();host.stdin.end('stop\n');},180000);
host.stdout.setEncoding('utf8');host.stdout.on('data',chunk=>{output+=chunk;const directory=output.match(/Auth bridge fixture: ([^\r\n]+)/)?.[1];if(!directory||started)return;started=true;
  child=spawn(process.execPath,[join(import.meta.dirname,'fixture.mjs'),directory],{cwd:root,windowsHide:true,env:{...browserEnvironment(),ANKT_E2E_NTS:process.env.ANKT_E2E_NTS==='true'?'true':'false',NODE_EXTRA_CA_CERTS:join(directory,'root-ca.pem')},stdio:['ignore','inherit','inherit']});
  child.once('exit',code=>{if(code!==0)process.exitCode=1;host.stdin.end('stop\n');});});
let error='';host.stderr.setEncoding('utf8');host.stderr.on('data',chunk=>{error+=chunk;});
const code=await done;clearTimeout(timer);if(code!==0||!started){console.error('Bridge fixture failed: '+error.split(/\r?\n/).slice(0,2).join(' ').slice(0,300));process.exitCode=1;}
