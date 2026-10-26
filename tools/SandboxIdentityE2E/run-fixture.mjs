import {spawn} from 'node:child_process';
import {resolve,join} from 'node:path';
import {browserEnvironment} from '../SandboxExecutor/cdp.mjs';
const root=resolve(import.meta.dirname,'../..');
const hostPath=resolve(root,process.argv[2]??'tools/SsoE2EHost/bin/Debug/net8.0/SsoE2EHost.dll');
const host=spawn(join(root,'.codex-tmp/sdk8-check/sdk/dotnet.exe'),[hostPath,'--sandbox-test-identity'],{cwd:root,windowsHide:true,env:browserEnvironment(),stdio:['pipe','pipe','pipe']});
let started=false,output='',child;
const done=new Promise(resolve=>host.once('exit',resolve));
const deadline=setTimeout(()=>{child?.kill();host.stdin.end('stop\n');},180000);
host.stdout.setEncoding('utf8');host.stdout.on('data',chunk=>{
  output+=chunk;const directory=output.match(/Sandbox identity fixture: ([^\r\n]+)/)?.[1];if(!directory||started)return;started=true;
  child=spawn(process.execPath,[join(import.meta.dirname,'fixture.mjs'),directory],{cwd:root,windowsHide:true,env:{...browserEnvironment(),NODE_EXTRA_CA_CERTS:join(directory,'root-ca.pem')},stdio:['ignore','inherit','inherit']});
  child.once('exit',code=>{process.exitCode=code??1;host.stdin.end('stop\n');});
});
let error='';host.stderr.setEncoding('utf8');host.stderr.on('data',chunk=>{error+=chunk;});
const code=await done;clearTimeout(deadline);
if(code!==0||!started){console.error('Isolated issuer failed: '+error.split(/\r?\n/).slice(0,2).join(' ').slice(0,320));process.exitCode=1;}
