import {createSandboxBridge} from './bridge.mjs';

const status=document.querySelector('#status'),preview=document.querySelector('#preview');
const match=location.pathname.match(/^\/sandbox-host\/([0-9a-f-]{36})(\/run)?$/i);
const controller=new AbortController();let timer,heartbeat,receive,bridge,frame;let stopped=false;
const request=async(path,body)=>{
  const response=await fetch(path,{method:body?'POST':'GET',credentials:'same-origin',cache:'no-store',redirect:'error',
    headers:body?{'Content-Type':'application/json'}:{},body:body?JSON.stringify(body):undefined,
    signal:AbortSignal.any([controller.signal,AbortSignal.timeout(10000)])});
  if(!response.ok)throw Object.assign(new Error('Sandbox request unavailable'),{status:response.status});return response.status===204?null:response.json();
};
function cleanup(){stopped=true;clearTimeout(timer);clearInterval(heartbeat);bridge?.dispose();frame?.remove();if(receive)window.removeEventListener('message',receive);}
function fail(){if(stopped)return;cleanup();status.textContent='Không xác nhận được kết nối hoặc phiên đã kết thúc. Kiểm tra cấu hình/CSP và mở lại từ Developer Portal.';}
window.addEventListener('pagehide',()=>{cleanup();controller.abort();},{once:true});

async function initialize(){
  if(!match)return;
  const id=match[1],parent=window.parent!==window?window.parent:window.opener,config=await request('/sandbox-host/config');
  if(stopped)return;
  const notify=type=>parent?.postMessage({channel:'ankt.sandbox.host',version:1,type,sessionId:id},config.portalOrigin);
  let closing=false;
  const close=async(type='host.closed')=>{
    if(closing)return;closing=true;cleanup();status.textContent='Đã dừng hiển thị. Đang thu hồi phiên…';
    try{await request('/sandbox-runtime/close',{});status.textContent='Đã đóng phiên Sandbox.';notify(type);}
    catch{status.textContent='Đã dừng hiển thị. Chưa xác nhận thu hồi phiên; vui lòng dừng lại từ Developer Portal.';notify('host.close_failed');closing=false;}
  };
  document.querySelector('#close').disabled=false;document.querySelector('#back').disabled=false;
  document.querySelector('#close').addEventListener('click',()=>void close());
  document.querySelector('#back').addEventListener('click',()=>void close('host.back'));
  if(!match[2]){
    if(!parent){fail();return;}
    const prepared=await request('/sandbox-runtime/prepare',{});
    if(stopped)return;
    if(typeof prepared.browserNonce!=='string'||!/^[A-Za-z0-9_-]{43}$/.test(prepared.browserNonce))throw new Error('Invalid browser preparation');
    let pending=false;
    receive=event=>{
      const data=event.data;
      if(stopped||pending||event.origin!==config.portalOrigin||event.source!==parent||!data||typeof data!=='object'||Array.isArray(data)||
        Object.keys(data).sort().join(',')!=='browserNonce,channel,grant,sessionId,type,version'||data.channel!=='ankt.sandbox.host'||data.version!==1||
        data.type!=='launch'||data.sessionId!==id||data.browserNonce!==prepared.browserNonce||typeof data.grant!=='string'||!/^[A-Za-z0-9_-]{43}$/.test(data.grant))return;
      pending=true;clearTimeout(timer);
      request('/sandbox-runtime/redeem',{sessionId:id,grant:data.grant,browserNonce:prepared.browserNonce})
        .then(()=>{if(!stopped)location.replace('/sandbox-host/'+id+'/run');}).catch(fail);
    };
    window.addEventListener('message',receive);timer=setTimeout(()=>{pending=true;fail();},30000);
    status.textContent='Đang chờ Developer Portal cho phép khởi chạy…';
    parent.postMessage({channel:'ankt.sandbox.host',version:1,type:'host.ready',sessionId:id,browserNonce:prepared.browserNonce},config.portalOrigin);
    return;
  }
  const context=await request('/sandbox-runtime/context');
  if(stopped)return;
  if(context.sessionId!==id||context.environment!=='Sandbox'||typeof context.partnerOrigin!=='string')throw new Error('Invalid Sandbox context');
  const website=new URL(context.websiteUrl);
  if(website.protocol!=='https:'||website.origin!==context.partnerOrigin||website.username||website.password)throw new Error('Invalid partner URL');
  document.querySelector('#app-name').textContent=context.name;
  frame=document.createElement('iframe');frame.title=context.name+' · Sandbox';frame.referrerPolicy='no-referrer';
  frame.setAttribute('sandbox','allow-scripts allow-same-origin allow-forms'+(context.ssoEnabled?' allow-popups allow-popups-to-escape-sandbox':''));
  frame.setAttribute('allow',"camera 'none'; microphone 'none'; geolocation 'none'; payment 'none'");
  let reportedConnection=false;
  bridge=createSandboxBridge({origin:context.partnerOrigin,source:()=>frame?.contentWindow,appId:context.appId,
    validate:()=>request('/sandbox-runtime/context'),send:message=>frame?.contentWindow?.postMessage(message,context.partnerOrigin),
    report:async value=>{try{await request('/sandbox-runtime/sdk-observations',{events:[value]});}
      catch(error){if(error.status===429||error.status===409)throw Object.assign(new Error(),{code:'BRIDGE_BUSY'});throw error;}},
    close:()=>request('/sandbox-runtime/close',{}),onClosed:()=>{setTimeout(()=>{cleanup();status.textContent='Đã đóng phiên Sandbox.';notify('host.closed');},50);},
    onConnected:()=>{clearTimeout(timer);status.textContent='Đã kết nối bridge · quan sát từ trình duyệt';notify('bridge.connected');
      if(!reportedConnection){reportedConnection=true;void request('/sandbox-runtime/host-observations',{events:[{category:'Bridge',code:'BRIDGE_READY'}]}).catch(()=>{});}}});
  receive=event=>{
    const value=event.data;
    if(event.source===parent&&event.origin===config.portalOrigin&&value&&typeof value==='object'&&!Array.isArray(value)&&
      Object.keys(value).sort().join(',')==='channel,sessionId,type,version'&&value.channel==='ankt.sandbox.host'&&value.version===1&&value.sessionId===id){
      if(value.type==='host.stop'){void close();return;}
      if(value.type==='host.reload'){bridge.dispose();frame.remove();location.reload();return;}
    }
    void bridge.handle(event);
  };
  window.addEventListener('message',receive);
  let loaded=false;
  const waitForBridge=()=>{
    clearTimeout(timer);
    timer=setTimeout(()=>{status.textContent='Chưa nhận được handshake. Website có thể chặn iframe hoặc chưa tích hợp SDK; kiểm tra CSP frame-ancestors cho Portal và host.';notify('bridge.timeout');},15000);
  };
  frame.addEventListener('load',()=>{
    if(stopped)return;
    if(loaded){bridge.reset();reportedConnection=false;status.textContent='Trang đã điều hướng. Đang chờ handshake mới…';notify('bridge.disconnected');waitForBridge();}
    loaded=true;
  });
  frame.src=website.href;preview.append(frame);status.textContent='Đang tải website. Chưa xác nhận runtime/bridge.';notify('session.connected');
  waitForBridge();
  heartbeat=setInterval(()=>{if(!stopped)void request('/sandbox-runtime/heartbeat',{}).catch(fail);},15000);
}
initialize().catch(fail);
