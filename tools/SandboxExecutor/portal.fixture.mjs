import https from 'node:https';
import {readFile} from 'node:fs/promises';
import {createRequire} from 'node:module';
import {resolve,join,sep,extname} from 'node:path';
import {fixtureFetch} from './fixture-fetch.mjs';
const require=createRequire(import.meta.url),frontend=resolve(import.meta.dirname,'../../../viora');
const {createDeveloperHandler}=require(join(frontend,'server/developer/handler.cjs'));
const {configureSandboxPolicy}=require(join(frontend,'server/developer/sandbox-policy.cjs'));
const base=require(join(frontend,'server/developer/vercel-base.json'));
export async function startPortal(directory,config,fetcher=fixtureFetch){
  const origin=config.sandboxPortalOrigin;
  const handler=createDeveloperHandler({siteOrigin:origin,apiOrigin:config.issuer,production:true,bffSecret:config.bffKey,clientIp:'127.0.0.1'},fetcher);
  const rules=configureSandboxPolicy(base,{ANKT_SANDBOX_HOST_ORIGIN:config.sandboxHostOrigin,DEVELOPER_SITE_ORIGIN:origin,EXPO_PUBLIC_OPEN_API_ISSUER:config.issuer}).headers
    .filter(x=>x.source.startsWith('/developers/')).map(x=>({...x,match:new RegExp('^'+x.source.replace(':path*','.*')+'$')}));
  const root=resolve(frontend,config.reviewFixture?'.codex-tmp/stage7-developer-final-export':'dist');
  const server=https.createServer({cert:await readFile(join(directory,'tls-cert.pem')),key:await readFile(join(directory,'tls-key.pem'))},async(req,res)=>{
    try{
      if(req.headers.host!==new URL(origin).host){res.writeHead(404);res.end();return;}
      const url=new URL(req.url,origin);for(const rule of rules)if(rule.match.test(url.pathname))for(const header of rule.headers)res.setHeader(header.key,header.value);
      if(url.pathname.startsWith('/developers/api/')){await handler(req,res);return;}
      let file=resolve(root,'.'+decodeURIComponent(url.pathname));
      if(!file.startsWith(root+sep)){res.writeHead(404);res.end();return;}
      if(!extname(file))file=join(root,'index.html');
      const mime={'.html':'text/html; charset=utf-8','.js':'text/javascript','.css':'text/css','.png':'image/png','.svg':'image/svg+xml','.json':'application/json','.woff2':'font/woff2'}[extname(file)]??'application/octet-stream';
      res.setHeader('Content-Type',mime);res.end(await readFile(file));
    }catch{if(!res.headersSent)res.writeHead(404);res.end();}
  });
  await new Promise((resolve,reject)=>{server.once('error',reject);server.listen(5494,'127.0.0.1',resolve);});
  return {close:()=>new Promise(resolve=>server.close(resolve))};
}
