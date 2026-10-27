import React from 'react';
import {createRoot} from 'react-dom/client';
import {AuthFixture,useAuth} from './nts-auth-context.fixture';
import {AnktAutoLoginGate} from '../../../Digital-Creative/Digital_Creative_App/components/ankt/AnktAutoLoginGate.web';
import RoleScreen from '../../../Digital-Creative/Digital_Creative_App/app/(auth)/ankt-role';

function Content(){
  const {session,pendingAnktSession}=useAuth(),current=session??pendingAnktSession;
  return <><pre id="partner-profile" hidden>{JSON.stringify(current?.user)}</pre>
    {pendingAnktSession?<RoleScreen/>:<main><h1>Đã đăng nhập Nghệ Thuật Số</h1><p>{session?.user.displayName}</p></main>}</>;
}
createRoot(document.getElementById('root')!).render(<AuthFixture><AnktAutoLoginGate><Content/></AnktAutoLoginGate></AuthFixture>);
