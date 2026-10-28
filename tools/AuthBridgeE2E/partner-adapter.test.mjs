import test from 'node:test';
import assert from 'node:assert/strict';
import {createPartnerAuth} from '../../docs/examples/miniapp-partner-backend.mjs';
function fixture(environment='Sandbox'){
  const clientId='app_'+'a'.repeat(43),stored=new Map(),accounts=new Map();let mode='ok',created=0;
  const identity={user:{id:'ankt-user',displayName:'Test'},miniAppId:clientId,environment,
    sessionId:'11111111-1111-4111-8111-111111111111',expiresAt:new Date(Date.now()+60000).toISOString()};
  const auth=createPartnerAuth({clientId,clientSecret:'s'.repeat(43),environment,apiOrigin:'https://identity.example',
    accounts:{async findOrCreate(value){const key=JSON.stringify([value.miniAppId,value.environment,value.user.id]);
      if(!accounts.has(key))accounts.set(key,{id:'partner-account-'+accounts.size});return accounts.get(key);}},
    sessions:{async create(value){created++;stored.set('partner-token',value);return {token:'partner-token'};},
      async get(token){return stored.get(token);},async revoke(token){stored.delete(token);}},
    fetchImpl:async(url,init)=>{
      assert.ok(url.startsWith('https://identity.example/api/miniapp/auth/partner/'));assert.equal(init.redirect,'error');
      assert.equal(Buffer.from(init.headers.Authorization.slice(6),'base64').toString(),clientId+':'+'s'.repeat(43));
      assert.equal(init.headers.Origin,undefined);
      return mode==='revoked'?Response.json({},{status:401}):mode==='offline'?Response.json({},{status:503}):Response.json(identity);
    }});
  return {auth,stored,accounts,identity,setMode:value=>mode=value,created:()=>created};
}
for(const env of ['Sandbox','Production'])test(`${env}: partner account upsert, own session, validated restore`,async()=>{
  const f=fixture(env);
  assert.deepEqual(await f.auth.login('c'.repeat(43)),{token:'partner-token'});
  assert.equal((await f.auth.restore('partner-token')).environment,env);
  await f.auth.login('d'.repeat(43));assert.equal(f.accounts.size,1);assert.equal(f.created(),2);
});
test('production backend rejects verified Sandbox identity before creating a partner account',async()=>{
  const f=fixture('Production');f.identity.environment='Sandbox';
  await assert.rejects(f.auth.login('c'.repeat(43)),/INVALID_IDENTITY/);assert.equal(f.created(),0);assert.equal(f.accounts.size,0);
});
test('revoked source deletes own partner session; offline fails closed without login loops',async()=>{
  const f=fixture();await f.auth.login('c'.repeat(43));f.setMode('offline');
  await assert.rejects(f.auth.restore('partner-token'),/ANKT_UNAVAILABLE/);assert.equal(f.stored.size,1);
  f.setMode('revoked');await assert.rejects(f.auth.restore('partner-token'),/SESSION_EXPIRED/);assert.equal(f.stored.size,0);
});
test('account substitution invalidates the previously issued partner session',async()=>{
  const f=fixture();await f.auth.login('c'.repeat(43));f.identity.user.id='other-account';
  await assert.rejects(f.auth.restore('partner-token'),/SESSION_EXPIRED/);assert.equal(f.stored.size,0);
});
