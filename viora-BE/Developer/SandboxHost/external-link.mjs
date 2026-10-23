export function createExternalLinkOpener({request,isStopped,document}) {
let externalPending=false;
return async function openExternal(value,signal){
  if(externalPending)throw Object.assign(new Error(),{code:'BRIDGE_BUSY'});
  externalPending=true;
  try{
    const approved=await request('/sandbox-runtime/external-links',{url:value});
    if(isStopped()||signal.aborted)throw new Error();
    const url=new URL(approved.url);
    const expiresAt=Date.parse(approved.expiresAt);
    if(url.href!==value||url.protocol!=='https:'||url.username||url.password||url.search||url.hash||!Number.isFinite(expiresAt)||expiresAt<=Date.now())throw new Error();
    await new Promise((resolve,reject)=>{
      const dialog=document.createElement('dialog'),title=document.createElement('h2'),address=document.createElement('p');
      const confirm=document.createElement('a'),cancel=document.createElement('button');
      const previous=document.activeElement;
      title.textContent='Mở liên kết ngoài?';title.id='external-link-title';dialog.setAttribute('aria-labelledby',title.id);
      address.textContent=url.href;confirm.textContent='Mở liên kết';confirm.href=url.href;confirm.target='_blank';confirm.rel='noopener noreferrer';
      cancel.textContent='Hủy';dialog.append(title,address,confirm,cancel);
      let done=false;
      const finish=accepted=>{
        if(done)return;done=true;clearTimeout(timeout);signal.removeEventListener('abort',abort);
        dialog.close();dialog.remove();if(previous?.isConnected)previous.focus();
        if(accepted)resolve();else reject(new Error());
      };
      const abort=()=>finish(false),timeout=setTimeout(abort,Math.min(15000,expiresAt-Date.now()));
      signal.addEventListener('abort',abort,{once:true});
      dialog.addEventListener('cancel',event=>{event.preventDefault();finish(false);});
      cancel.addEventListener('click',()=>finish(false));
      confirm.addEventListener('click',event=>{
        if(!event.isTrusted||signal.aborted||isStopped()||Date.now()>=expiresAt){event.preventDefault();finish(false);return;}
        // Native link activation preserves the user's gesture; no code/token crosses this bridge.
        finish(true);
      });
      document.body.append(dialog);dialog.showModal();cancel.focus();
    });
  }finally{externalPending=false;}
}
}
