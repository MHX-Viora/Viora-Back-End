import type {PropsWithChildren} from 'react';
export const RealtimeProvider=({children}:PropsWithChildren)=>children;
export const InAppMessageBanner=()=>null;
export const PushNotificationBridge=()=>null;
export const offlineStore={clear:async()=>{}};
export const pushNotificationService={unregister:async()=>{}};
export const googleSignInService={signOut:async()=>{}};
const forbidden=async()=>{throw Error('Embedded login touched standalone session storage');};
export const sessionStorage={read:forbidden,write:forbidden,clear:forbidden,canRestoreFromRefreshCookie:()=>false,markRefreshCookieRejected:forbidden};
