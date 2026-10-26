// Actual BFF authentication and production Sandbox account component, isolated test data.
import { useEffect, useState } from 'react';
import { createRoot } from 'react-dom/client';
import { accountRequest } from '../../../viora/features/developer/lib/account-client';
import { MiniAppSandboxAccount } from '../../../viora/features/developer/components/mini-app-sandbox-account';
import '../../../viora/features/developer/developer.css';
import '../../../viora/features/developer/components/mini-app-sandbox.css';
function Portal() {
  const [loggedIn,setLoggedIn]=useState(false),[app,setApp]=useState(null),[error,setError]=useState('');
  const appId=location.pathname.split('/').at(-2);
  useEffect(()=>{if(loggedIn)void accountRequest('mini-apps/'+appId).then(setApp).catch(()=>setError('Application lookup failed'));},[loggedIn,appId]);
  async function login(event){event.preventDefault();const data=new FormData(event.currentTarget);try{await accountRequest('auth/login','POST',{email:data.get('email'),password:data.get('password'),rememberMe:false});setLoggedIn(true);}catch{setError('Developer login failed');}}
  return <div className="developer-site"><main style={{maxWidth:1000,margin:'auto',padding:20}}>
    <h1>ANKT Sandbox Test</h1>{error&&<p role="alert">{error}</p>}
    {!loggedIn?<form onSubmit={login}><label>Email<input name="email" type="email" required/></label><label>Mật khẩu<input name="password" type="password" required/></label><button className="button">Đăng nhập Developer</button></form>:app?<MiniAppSandboxAccount app={app}/>:<p>Đang tải…</p>}
  </main></div>;
}
createRoot(document.getElementById('root')).render(<Portal/>);
