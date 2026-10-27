import {useState} from 'react';
import {createRoot} from 'react-dom/client';
import {accountRequest} from '../../../viora/features/developer/lib/account-client';
import {MiniAppAuthBridgePreview} from '../../../viora/features/developer/components/mini-app-auth-bridge-preview';
import {MiniAppBridgeIntegration} from '../../../viora/features/developer/components/mini-app-bridge-integration';
import AuthHost from '../../../viora/features/mini-apps/adapters/auth-host.web';
import '../../../viora/features/developer/developer.css';
import '../../../viora/features/developer/mini-app.css';
function Portal(){
  const [loggedIn,setLoggedIn]=useState(false),[error,setError]=useState(''),[status,setStatus]=useState('');
  const appId=new URL(location.href).searchParams.get('app');
  if(location.pathname==='/production')return <><p id="production-status">{status}</p><div style={{height:720}}><AuthHost appId={appId} attempt={1} onStatus={setStatus}/></div></>;
  if(!loggedIn)return <form onSubmit={async event=>{event.preventDefault();const form=new FormData(event.currentTarget);try{await accountRequest('auth/login','POST',{email:form.get('email'),password:form.get('password')});setLoggedIn(true);}catch(error){setError(error.message);}}}>
    <label>Email<input name="email"/></label><label>Mật khẩu<input name="password" type="password"/></label><button>Đăng nhập Developer</button><p role="alert">{error}</p></form>;
  return <><MiniAppAuthBridgePreview appId={appId}/><MiniAppBridgeIntegration appId={appId}/></>;
}
createRoot(document.getElementById('root')).render(<div className="developer-site"><main style={{fontFamily:'Arial, sans-serif',padding:20,maxWidth:1100,margin:'auto'}}><Portal/></main></div>);
