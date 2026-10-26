const channel='ankt.miniapp';
const methods=['platform.getContext','platform.close','platform.getEnvironment','platform.isInsideAnkt'];
const record=value=>value!==null&&typeof value==='object'&&!Array.isArray(value);
const uuid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
const observations={Runtime:['SDK_INITIALIZED','RUNTIME_READY','RUNTIME_ERROR'],Bridge:['BRIDGE_READY','BRIDGE_ERROR'],Network:['NETWORK_ERROR'],Permission:['PERMISSION_DENIED']};
const diagnostic=value=>record(value)&&['category','code'].every(key=>Object.hasOwn(value,key))&&
  Object.keys(value).every(key=>['category','code','count'].includes(key))&&typeof value.category==='string'&&Object.hasOwn(observations,value.category)&&
  observations[value.category].includes(value.code)&&(!Object.hasOwn(value,'count')||Number.isInteger(value.count)&&value.count>=0&&value.count<=1000000);

export function createSandboxBridge({origin,source,appId,validate,send,close,report,openExternal,onConnected=()=>{},onClosed=()=>{}}) {
  let disposed=false,closing=false,generation=0,inFlight=0;const ready=new Set(),seen=new Set();let timestamps=[],actions=new AbortController();
  const reply=(data,ok,value)=>send({channel,version:data.version,type:'response',id:data.id,ok,...(ok?{result:value}:{error:{code:value}})});
  function reset(){generation++;ready.clear();actions.abort();actions=new AbortController();}
  function dispose(){disposed=true;reset();seen.clear();timestamps=[];}
  async function handle(event){
    const data=event.data;
    if(disposed||!event.source||event.origin!==origin||event.source!==source()||!record(data)||data.channel!==channel||
      ![1,2].includes(data.version)||data.type!=='request'||!uuid(data.id))return;
    if(!['channel','version','type','id','method'].every(key=>Object.hasOwn(data,key))||
      Object.keys(data).some(key=>!['channel','version','type','id','method','params'].includes(key))||typeof data.method!=='string'||data.method.length>64){reply(data,false,'BRIDGE_PROTOCOL');return;}
    if(closing){reply(data,false,'BRIDGE_FORBIDDEN');return;}
    if(seen.has(data.id)){reply(data,false,'BRIDGE_PROTOCOL');return;}
    const now=Date.now();timestamps=timestamps.filter(time=>time>now-60000);
    if(inFlight>=8||seen.size>=1024||timestamps.length>=64){reply(data,false,'BRIDGE_BUSY');return;}
    seen.add(data.id);timestamps.push(now);
    const permissions=data.version===1?methods.slice(0,2):[...methods,...(report?['diagnostics.report']:[]),...(openExternal?['platform.openExternalUrl']:[])];
    if(data.method!=='bridge.handshake'&&!permissions.includes(data.method)){reply(data,false,'UNSUPPORTED_METHOD');return;}
    const validParams=data.method==='diagnostics.report'?diagnostic(data.params):data.method==='platform.openExternalUrl'?
      record(data.params)&&Object.keys(data.params).join(',')==='url'&&typeof data.params.url==='string'&&data.params.url.length<=2048:!Object.hasOwn(data,'params');
    if(!validParams){reply(data,false,'BRIDGE_PROTOCOL');return;}
    if(data.method!=='bridge.handshake'&&!ready.has(data.version)){reply(data,false,'BRIDGE_FORBIDDEN');return;}
    const current=generation;inFlight++;
    try{
      const context=await validate();if(disposed||generation!==current)return;
      if(closing){reply(data,false,'BRIDGE_FORBIDDEN');return;}
      if(context.appId!==appId||context.environment!=='Sandbox')throw new Error('Invalid runtime context');
      switch(data.method){
        case 'bridge.handshake':ready.add(data.version);reply(data,true,{permissions:[...permissions]});onConnected(data.version);break;
        case 'platform.getContext':reply(data,true,{appId,environment:'Sandbox',runtime:data.version===1?'controlled-web':'sandbox-web'});break;
        case 'platform.getEnvironment':reply(data,true,'Sandbox');break;
        case 'platform.isInsideAnkt':reply(data,true,true);break;
        case 'diagnostics.report':await report(data.params);if(disposed||generation!==current)return;reply(data,true,null);break;
        case 'platform.openExternalUrl':await openExternal(data.params.url,actions.signal);if(disposed||generation!==current||closing)return;reply(data,true,null);break;
        case 'platform.close':closing=true;actions.abort();await close();if(disposed||generation!==current)return;reply(data,true,null);dispose();onClosed();break;
      }
    }catch(error){
      if(!disposed&&generation===current){
        if(data.method==='platform.close'){closing=false;actions=new AbortController();}
        if(data.method==='platform.openExternalUrl')reply(data,false,error?.code==='BRIDGE_BUSY'?'BRIDGE_BUSY':'BRIDGE_FORBIDDEN');
        else if(data.method==='diagnostics.report'&&error?.code==='BRIDGE_BUSY')reply(data,false,'BRIDGE_BUSY');
        else{ready.delete(data.version);reply(data,false,'BRIDGE_FORBIDDEN');}
      }
    }finally{inFlight--;}
  }
  return {handle,reset,dispose};
}
