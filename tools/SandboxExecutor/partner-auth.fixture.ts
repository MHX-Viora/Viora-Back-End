import {createMiniAppSdk,connectHostBridge,completePopupRedirect} from '../../../viora/packages/miniapp-sdk/src/index';
const config=await (await fetch('/config.json',{cache:'no-store'})).json();
const output=document.querySelector<HTMLParagraphElement>('#status')!;
let runId=config.runId;
if(location.pathname==='/callback')completePopupRedirect(config.redirectUri);
else{
  try{
    const bridge=await connectHostBridge(config.hostOrigin,{protocolVersion:2});
    const context=await bridge.getContext();if(context.environment!=='Sandbox')throw Error();
    document.querySelector('#context')!.textContent=await bridge.getEnvironment()+' · inside '+await bridge.isInsideAnkt();
    document.querySelector('#external')!.addEventListener('click',()=>{
      document.querySelector('#bridge-result')!.textContent='confirming';
      void bridge.openExternalUrl('https://partner.ankt.example.com/help').then(()=>{document.querySelector('#bridge-result')!.textContent='opened';},()=>{document.querySelector('#bridge-result')!.textContent='cancelled';});
    });
    document.querySelector('#close')!.addEventListener('click',()=>{void bridge.close().catch(()=>{});});
    if(config.clientType==='Confidential'){
      await import('/embedded.js');
      const current=await fetch('/session',{credentials:'same-origin',cache:'no-store',redirect:'error'});
      output.textContent=current.ok?'authenticated':'unauthenticated';
    }
    else{
      const sdk=createMiniAppSdk({issuer:config.issuer,clientId:config.clientId,redirectUri:config.redirectUri,scope:'openid profile email'});
      await sdk.initialize();output.textContent='unauthenticated';
      let stopped=false,timer:ReturnType<typeof setTimeout>;
      const refresh=async()=>{try{runId=(await (await fetch('/config.json',{cache:'no-store'})).json()).runId;document.querySelector('#run-ready')!.textContent=runId?'ready':'waiting';}catch{runId=null;}
        if(!stopped)timer=setTimeout(()=>void refresh(),500);};void refresh();
      window.addEventListener('pagehide',()=>{stopped=true;clearTimeout(timer);},{once:true});
      for(const name of ['login','force'])document.querySelector('#'+name)!.addEventListener('click',()=>{
        output.textContent='awaiting_authorization';
        // The click opens the popup synchronously; the server independently validates the hint.
        void sdk.auth.loginPopup({sandboxRunId:runId??undefined,prompt:name==='force'?'consent':undefined}).then(async()=>{
          await sdk.auth.getUser();output.textContent='authenticated';
        }).catch(()=>{output.textContent='request_failed';});
      });
      document.querySelector('#logout')!.addEventListener('click',()=>{sdk.auth.logout();output.textContent='unauthenticated';});
    }
    document.body.dataset.fixtureReady='true';
  }catch{output.textContent='bridge_unavailable';}
}
