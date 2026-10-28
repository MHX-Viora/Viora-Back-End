// Server only. Supply your existing account repository and session store.
export function createPartnerAuth({apiOrigin,clientId,clientSecret,environment,accounts,sessions,fetchImpl=fetch}) {
  const url=new URL(apiOrigin);
  if(url.protocol!=='https:'||url.origin!==apiOrigin||!/^app_[A-Za-z0-9_-]{43}$/.test(clientId)||
    typeof clientSecret!=='string'||clientSecret.length<32||!['Sandbox','Production'].includes(environment))throw Error('INVALID_CONFIG');
  const authorization='Basic '+Buffer.from(clientId+':'+clientSecret).toString('base64');
  async function verify(path,body) {
    const response=await fetchImpl(apiOrigin+'/api/miniapp/auth/partner/'+path,{method:'POST',redirect:'error',cache:'no-store',
      signal:AbortSignal.timeout(10000),headers:{'Content-Type':'application/json',Authorization:authorization},body:JSON.stringify(body)});
    if(!response.ok)throw Error(response.status>=500||response.status===429?'ANKT_UNAVAILABLE':'SESSION_EXPIRED');
    const identity=await response.json();
    if(identity?.miniAppId!==clientId||identity.environment!==environment||typeof identity.user?.id!=='string'||
      typeof identity.user?.displayName!=='string'||!/^[-0-9a-f]{36}$/i.test(identity.sessionId)||
      !Number.isFinite(Date.parse(identity.expiresAt))||Date.parse(identity.expiresAt)<=Date.now())throw Error('INVALID_IDENTITY');
    return identity;
  }
  return {
    async login(code) {
      if(typeof code!=='string'||!/^[A-Za-z0-9_-]{43}$/.test(code))throw Error('INVALID_CODE');
      const identity=await verify('exchange',{code});
      const account=await accounts.findOrCreate({miniAppId:clientId,environment,user:identity.user});
      // Store grant metadata server-side, never trust environment/user supplied by a browser.
      return sessions.create({accountId:account.id,anktUserId:identity.user.id,miniAppId:clientId,environment,
        grantId:identity.sessionId,expiresAt:identity.expiresAt});
    },
    async restore(token) {
      const session=await sessions.get(token);
      if(!session||session.miniAppId!==clientId||session.environment!==environment||!Number.isFinite(Date.parse(session.expiresAt))||Date.parse(session.expiresAt)<=Date.now())throw Error('SESSION_EXPIRED');
      try {
        const identity=await verify('session',{sessionId:session.grantId});
        if(identity.sessionId!==session.grantId||identity.user.id!==session.anktUserId)throw Error('SESSION_EXPIRED');
        return {accountId:session.accountId,user:identity.user,environment};
      } catch(error) {
        if(error.message!=='ANKT_UNAVAILABLE')await sessions.revoke(token);
        throw error;
      }
    },
  };
}
