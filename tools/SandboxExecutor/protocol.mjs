const guid=value=>typeof value==='string'&&/^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
const record=value=>value!==null&&typeof value==='object'&&!Array.isArray(value);
function origin(value){const url=new URL(value);if(url.protocol!=='https:'||url.origin!==value||url.username||url.password)throw new Error('Invalid executor job');return url;}
export function validateJob(value){
  if(!record(value)||!guid(value.runId)||!guid(value.sessionId)||typeof value.appId!=='string'||!/^[a-zA-Z0-9_-]{1,100}$/.test(value.appId)||
    !/^[A-Fa-f0-9]{64}$/.test(value.configurationFingerprint)||!Number.isFinite(Date.parse(value.deadlineAt))||Date.parse(value.deadlineAt)<=Date.now())throw new Error('Invalid executor job');
  const host=origin(value.hostOrigin),portal=origin(value.portalOrigin),partner=origin(value.partnerOrigin),website=new URL(value.websiteUrl);
  if(new Set([host.hostname,portal.hostname,partner.hostname]).size!==3||website.protocol!=='https:'||website.origin!==partner.origin||website.username||website.password)throw new Error('Invalid executor job');
  if(!Array.isArray(value.checks)||value.checks.length<1||value.checks.length>2||new Set(value.checks.map(x=>x.key)).size!==value.checks.length||value.checks.some(x=>
    !record(x)||!['runtime','bridge'].includes(x.key)||typeof x.challenge!=='string'||x.challenge.length<1||x.challenge.length>4096||
    !Number.isFinite(Date.parse(x.expiresAt))||Date.parse(x.expiresAt)<=Date.now()||Date.parse(x.expiresAt)>Date.parse(value.deadlineAt)))throw new Error('Invalid executor job');
  return value;
}
export function validateSignal(payload,context,mainFrame,job){
  if(!context||context.origin!==job.portalOrigin||context.auxData?.frameId!==mainFrame||context.auxData?.isDefault!==true||typeof payload!=='string'||payload.length>1024)return null;
  let value;try{value=JSON.parse(payload);}catch{return null;}
  if(!record(value)||value.sessionId!==job.sessionId)return null;
  const keys=Object.keys(value).sort().join(',');
  if(value.type==='host.ready'&&keys==='browserNonce,sessionId,type'&&/^[A-Za-z0-9_-]{43}$/.test(value.browserNonce))return value;
  return value.type==='bridge.connected'&&keys==='sessionId,type'?value:null;
}
export function hasPartnerFrame(tree,job){
  if(!tree||new URL(tree.frame.url).origin!==job.portalOrigin)return false;
  return (tree.childFrames??[]).some(host=>host.frame.url===job.hostOrigin+'/sandbox-host/'+job.sessionId+'/run'&&
    (host.childFrames??[]).some(partner=>{
      try{return new URL(partner.frame.url).origin===job.partnerOrigin&&partner.frame.securityOrigin===job.partnerOrigin;}catch{return false;}
    }));
}
export function mergeFrameTrees(root,related){
  const frames=new Map();
  const collect=tree=>{if(!tree?.frame)return;const prior=frames.get(tree.frame.id);
    frames.set(tree.frame.id,{...prior,...tree,childFrames:[...(prior?.childFrames??[]),...(tree.childFrames??[])]});
    for(const child of tree.childFrames??[])collect(child);
  };
  collect(root);related.forEach(collect);
  const expand=(id,seen=new Set())=>{const tree=frames.get(id);if(!tree||seen.has(id))return null;
    const next=new Set([...seen,id]),ids=new Set((tree.childFrames??[]).map(x=>x.frame.id));
    for(const item of frames.values())if(item.frame.parentId===id)ids.add(item.frame.id);
    return {...tree,childFrames:[...ids].map(child=>expand(child,next)).filter(Boolean)};
  };
  return expand(root.frame.id);
}
