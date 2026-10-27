import {createRequire} from 'node:module';
import {resolve,join} from 'node:path';
import {readFile,writeFile} from 'node:fs/promises';
import assert from 'node:assert/strict';

export async function buildNts(config,options){
  const root=resolve(import.meta.dirname,'../../../Digital-Creative/Digital_Creative_App');
  const require=createRequire(resolve(import.meta.dirname,'../../../viora/package.json')),{build}=require('esbuild');
  const assets=new Map();
  for(const key of ['a','b']){
    const bundle=await build({...options,entryPoints:[join(import.meta.dirname,'nts-miniapp.fixture.tsx')],outfile:'nts-'+key+'.js',
      tsconfig:join(root,'tsconfig.json'),resolveExtensions:['.web.tsx','.web.ts','.web.js','.tsx','.ts','.jsx','.js','.json'],
      loader:{'.ttf':'dataurl','.js':'jsx'},nodePaths:[join(root,'node_modules'),join(root,'node_modules/expo/node_modules'),...options.nodePaths],
      alias:{...options.alias,'react-native':join(root,'node_modules/react-native-web'),
        'expo-router':join(import.meta.dirname,'nts-router.fixture.ts'),
        ...Object.fromEntries(['@/store/realtime-context','@/components/chat/InAppMessageBanner','@/components/notification/PushNotificationBridge',
          '@/services/offline-store','@/services/push-notification.service','@/services/google-sign-in.service','@/services/session-storage']
          .map(key=>[key,join(import.meta.dirname,'nts-effects.fixture.tsx')])),
        '@/store/auth-context':join(import.meta.dirname,'nts-auth-context.fixture.tsx')},
      define:{...options.define,'__DEV__':'false','process.env':'{}','process.env.EXPO_OS':'"web"','process.env.NODE_ENV':'"production"','process.env.EXPO_PUBLIC_API_URL':JSON.stringify(config.partner+'/fixture/'+key),
        'process.env.EXPO_PUBLIC_ANKT_CLIENT_ID':JSON.stringify('app_'+key.repeat(43)),
        'process.env.EXPO_PUBLIC_ANKT_API_ORIGIN':JSON.stringify(config.api),
        'process.env.EXPO_PUBLIC_ANKT_HOST_ORIGIN':JSON.stringify(config.portal)}});
    for(const file of bundle.outputFiles)assets.set('/'+file.path.split(/[\\/]/).at(-1),file.contents);
  }
  return assets;
}

export async function runNtsBrowser({cdp,login,config,attach,evaluate,wait,exchanges}){
  async function state(tab){return wait(async()=>{
    const child=(await cdp.send('Target.getTargets')).targetInfos.find(info=>info.type==='iframe'&&info.browserContextId===tab.browserContextId&&info.url.startsWith(config.partner));
    if(!child)return null;
    const id=await attach(child.targetId);
    const text=await evaluate(id,"document.querySelector('#partner-profile')?.textContent");
    return text?{user:JSON.parse(text),id}:null;
  });}
  const [a,duplicate,b]=await Promise.all([login(config.accounts[0]),login(config.accounts[0]),login(config.accounts[1])]);
  const first=await state(a),same=await state(duplicate),other=await state(b);
  assert.equal(first.user.id,same.user.id);
  assert.equal(first.user.role,'UNASSIGNED');assert.equal(first.user.onboardingStatus,'ROLE_REQUIRED');
  assert.equal(other.user.role,'UNASSIGNED');
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1280,height:900,deviceScaleFactor:1,mobile:false},a.sessionId);
  const screenshot=await cdp.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},a.sessionId);
  await writeFile(resolve(import.meta.dirname,'../../.codex-tmp/nts-role-desktop.png'),Buffer.from(screenshot.data,'base64'));
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:375,height:812,deviceScaleFactor:1,mobile:true},a.sessionId);
  const mobile=await cdp.send('Page.captureScreenshot',{format:'png',captureBeyondViewport:true},a.sessionId);
  await writeFile(resolve(import.meta.dirname,'../../.codex-tmp/nts-role-mobile.png'),Buffer.from(mobile.data,'base64'));
  await cdp.send('Emulation.setDeviceMetricsOverride',{width:1280,height:900,deviceScaleFactor:1,mobile:false},a.sessionId);
  for(const [tab,label,role] of [[a,'Tôi muốn cho thuê','CREATIVE'],[b,'Tôi muốn đi thuê','CLIENT']]){
    const current=await state(tab);
    await evaluate(current.id,`document.querySelector('[aria-label="${label}"]').click();true`);
    await wait(async()=>(await state(tab)).user.role===role);
  }
  console.log('PASS concurrent first logins resolve one linked Nghệ Thuật Số account; real SDK/gateway/JWT/role screen saves Provider/Creative and Renter/Client');
  let before=exchanges();
  await evaluate(a.sessionId,"[...document.querySelectorAll('button')].find(b=>b.textContent==='Mở lại Sandbox').click();true");
  await wait(()=>exchanges()>before);
  await wait(async()=>(await state(a)).user.role==='CREATIVE');assert.equal((await state(a)).user.id,first.user.id);
  before=exchanges();const child=await state(a);await evaluate(child.id,'setTimeout(()=>location.reload(),0);true');
  await wait(()=>exchanges()>before);
  await wait(async()=>(await state(a)).user.role==='CREATIVE');
  const anotherDevice=await login(config.accounts[0]);
  await wait(async()=>(await state(anotherDevice)).user.role==='CREATIVE');
  assert.equal((await state(anotherDevice)).user.id,first.user.id);
  console.log('PASS close/reopen, iframe refresh and separate browser context reuse the same Nghệ Thuật Số account and saved role');
  before=exchanges();let current=await state(a);
  await evaluate(current.id,"window.dispatchEvent(new Event('ankt:session-expired'));true");
  await wait(()=>exchanges()>before);await wait(async()=>(await state(a)).user.role==='CREATIVE');
  before=exchanges();current=await state(a);
  await evaluate(current.id,"window.dispatchEvent(new Event('ankt:session-expired'));true");
  await wait(()=>evaluate(current.id,"document.body.innerText.includes('Chưa thể đăng nhập Nghệ Thuật Số')"));
  assert.equal(exchanges(),before);
  await evaluate(current.id,"[...document.querySelectorAll('[role=button]')].find(button=>button.textContent==='Thử lại').click();true");
  await wait(()=>exchanges()>before);await wait(async()=>(await state(a)).user.role==='CREATIVE');
  console.log('PASS rapid repeated session-expiry notifications stop automatic login; explicit Retry restores the saved account');
}
