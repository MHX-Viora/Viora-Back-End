// Fixed fixture-only routing. TLS trusts the disposable CA; application/proxy code is unchanged.
import https from 'node:https';
export function fixtureFetch(input,init){
  const url=new URL(String(input));
  if(url.origin==='https://identity.ankt.example.com'){
    const target=new URL(url.pathname+url.search,'https://127.0.0.1:5490');
    // Node fetch rewrites Host; use an explicit TLS request for this private reverse proxy.
    return new Promise((resolve,reject)=>{
      const body=init?.body instanceof URLSearchParams?init.body.toString():init?.body;
      const request=https.request(target,{method:init?.method??'GET',headers:{...Object.fromEntries(new Headers(init?.headers)),Host:'identity.ankt.example.com',...(body!=null?{'Content-Length':Buffer.byteLength(body)}:{})},signal:init?.signal},response=>{
        const chunks=[];let length=0;
        response.on('data',chunk=>{length+=chunk.length;if(length>262144){request.destroy(new Error('Fixture response too large'));return;}chunks.push(chunk);});
        response.on('end',()=>{const headers=new Headers();for(let i=0;i<response.rawHeaders.length;i+=2)headers.append(response.rawHeaders[i],response.rawHeaders[i+1]);
          if(url.pathname.includes('/sandbox/test-runs')&&response.statusCode>=400){
            let code='UNKNOWN';try{const value=JSON.parse(Buffer.concat(chunks)).error?.code;if(/^[A-Z_]{1,64}$/.test(value))code=value;}catch{}
            console.log('FIXTURE_RUN_HTTP_'+(init?.method??'GET')+'_'+response.statusCode+'_'+code+'_'+(url.pathname.endsWith('test-runs')?'LIST':'DETAIL'));
          }
          if(init?.redirect==='error'&&response.statusCode>=300&&response.statusCode<400){reject(new Error('Fixture redirect rejected'));return;}
          resolve(new Response([204,205,304].includes(response.statusCode)?null:Buffer.concat(chunks),{status:response.statusCode,headers}));});
      });
      const preflight=/\/(?:review-readiness|publication-readiness|reviews|publish)$/.test(url.pathname);
      request.setTimeout(preflight?45000:10000,()=>request.destroy(new Error('Fixture request timed out')));request.on('error',reject);
      request.end(body);
    });
  }
  return fetch(input,init);
}
