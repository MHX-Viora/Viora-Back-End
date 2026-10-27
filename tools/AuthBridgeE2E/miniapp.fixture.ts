import type {createAnktMiniApp as CreateAnktMiniApp} from '../../../viora/packages/miniapp-sdk/src/host-auth';
declare const ANKTMiniApp:{create:typeof CreateAnktMiniApp};
const createAnktMiniApp=ANKTMiniApp.create;
const index=location.pathname.split('/')[1];
// Chrome fixture: choose only these two hardcoded trusted test hosts.
const hostOrigin=Array.from(location.ancestorOrigins).includes('http://localhost:5495')?'http://localhost:5495':'https://portal.ankt.localhost:5494';
const ANKT=createAnktMiniApp({apiOrigin:'https://identity.ankt.example.com:5490',hostOrigin,clientId:'app_'+index.repeat(43)});
async function start(){try{await ANKT.init();const user=await ANKT.auth.getUserInfo();
  const {code}=await ANKT.auth.getLoginCode();
  const response=await fetch('/api/ankt/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({clientId:'app_'+index.repeat(43),code})});
  if(!response.ok)throw Error('Partner login failed');const identity=await response.json();
  if(identity.user.id!==user.id||identity.environment!==(user.isSandbox?'Sandbox':'Production'))throw Error('Partner identity mismatch');
  document.getElementById('profile')!.textContent=JSON.stringify(user);
  document.getElementById('name')!.textContent=user.displayName;
  document.getElementById('username')!.textContent='@'+user.username;
  document.getElementById('kind')!.textContent=user.isSandbox?'Đã đăng nhập Sandbox':'Đã đăng nhập ANKT';
  document.getElementById('session')!.textContent=JSON.stringify(await ANKT.auth.getSession());}catch(error){document.getElementById('error')!.textContent=(error as Error).message;}}
void start();
