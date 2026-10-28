import {spawn} from 'node:child_process';
import {mkdtemp,readFile,rm} from 'node:fs/promises';
import {tmpdir} from 'node:os';
import {join,resolve,sep} from 'node:path';
import {setTimeout as delay} from 'node:timers/promises';

// Native CDP, no dependency/download or shared/user browser profile.
export function browserEnvironment(input=process.env){
  const allowed=['SystemRoot','WINDIR','TEMP','TMP','LOCALAPPDATA','APPDATA','USERPROFILE','HOMEDRIVE','HOMEPATH','ProgramFiles','ProgramFiles(x86)','HOME','LANG','LC_ALL'];
  return Object.fromEntries(Object.entries(input).filter(([key])=>allowed.some(value=>value.toLowerCase()===key.toLowerCase())));
}
export async function launchChrome(executable,args=[]){
  if(typeof executable!=='string'||!executable)throw new Error('Executor browser unavailable');
  const profile=await mkdtemp(join(tmpdir(),'ankt-sandbox-executor-'));
  const process=spawn(executable,['--headless=new','--remote-debugging-address=127.0.0.1','--remote-debugging-port=0','--no-first-run','--no-default-browser-check',
    '--user-data-dir='+profile,...args,'about:blank'],{stdio:'ignore',windowsHide:true,env:browserEnvironment()});
  let exited=false;process.on('error',()=>{exited=true;});process.on('exit',()=>{exited=true;});
  let client;
  const close=async()=>{
    try{await client?.send('Browser.close');}catch{/* Already closed. */}client?.close();
    if(!exited)await Promise.race([new Promise(resolve=>process.once('exit',resolve)),delay(2000)]);
    if(!exited){process.kill();await Promise.race([new Promise(resolve=>process.once('exit',resolve)),delay(2000)]);}
    const target=resolve(profile),allowed=resolve(tmpdir())+sep;
    if(!target.startsWith(allowed)||!target.split(sep).at(-1).startsWith('ankt-sandbox-executor-'))throw new Error('Invalid executor profile');
    await rm(target,{recursive:true,force:true,maxRetries:5,retryDelay:100});
  };
  try{
    let port,path;
    for(let attempt=0;attempt<100&&!exited;attempt++){
      try{[port,path]=(await readFile(join(profile,'DevToolsActivePort'),'utf8')).trim().split(/\r?\n/);break;}catch{await delay(100);}
    }
    if(!/^\d{1,5}$/.test(port??'')||!path?.startsWith('/devtools/browser/'))throw new Error('Executor browser unavailable');
    const websocket='ws://127.0.0.1:'+port+path;
    client=await Cdp.connect(websocket);
    if(globalThis.process.env.ANKT_E2E_TRACK_BROWSERS==='1'&&globalThis.process.send)
      globalThis.process.send({type:'fixture-browser',pid:process.pid,profile,websocket});
    return {client,close};
  }catch{await close();throw new Error('Executor browser unavailable');}
}
class Cdp{
  #id=0;#pending=new Map();#events=new Set();
  constructor(socket){
    this.socket=socket;socket.addEventListener('message',event=>{
      let value;try{value=JSON.parse(event.data);}catch{return;}
      const pending=this.#pending.get(value.id);
      if(pending){this.#pending.delete(value.id);clearTimeout(pending.timer);value.error?pending.reject(new Error('Browser command failed: '+pending.method+' ('+value.error.code+')')):pending.resolve(value.result);}
      else for(const listener of this.#events)listener(value);
    });
    socket.addEventListener('close',()=>{for(const item of this.#pending.values()){clearTimeout(item.timer);item.reject(new Error('Browser closed'));}this.#pending.clear();});
  }
  static async connect(url){const socket=new WebSocket(url);await new Promise((resolve,reject)=>{socket.addEventListener('open',resolve,{once:true});socket.addEventListener('error',()=>reject(new Error('Browser unavailable')),{once:true});});return new Cdp(socket);}
  send(method,params={},sessionId){return new Promise((resolve,reject)=>{
    const timeoutError=new Error('Browser command timed out: '+method);
    const id=++this.#id,timer=setTimeout(()=>{this.#pending.delete(id);reject(timeoutError);},10000);
    this.#pending.set(id,{resolve,reject,timer,method});this.socket.send(JSON.stringify({id,method,params,...(sessionId?{sessionId}:{})}));
  });}
  on(listener){this.#events.add(listener);return()=>this.#events.delete(listener);}
  close(){this.socket.close();}
}
