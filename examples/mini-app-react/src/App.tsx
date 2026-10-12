import React, { useEffect, useState } from 'react';
import type { PartnerProfile } from '../../../viora-BE/wwwroot/mini-app-sdk/ankt-mini-app';
let initializing: Promise<void> | undefined;
function initialize() {
  return initializing ??= (async () => {
    const sdkUrl = new URL(import.meta.env.VITE_ANKT_SDK_URL); const host = new URL(import.meta.env.VITE_ANKT_HOST_ORIGIN);
    if (sdkUrl.protocol !== 'https:' || sdkUrl.pathname !== '/mini-app-sdk/ankt-mini-app.js' || host.protocol !== 'https:' || host.pathname !== '/') throw new Error('Cấu hình URL SDK và ANKT host HTTPS chính xác.');
    await new Promise<void>((resolve, reject) => { const script = document.createElement('script'); script.src = sdkUrl.href; script.onload = () => resolve(); script.onerror = () => { script.remove(); reject(new Error('Không tải được SDK.')); }; document.head.appendChild(script); });
    window.ANKT.configure({ hostOrigin: host.origin, bff: true });
    if (window.parent !== window || window.opener || 'ReactNativeWebView' in window) await window.ANKT.auth.autoLogin();
  })().catch(error => { initializing = undefined; throw error; });
}
export function App() {
  const [profile, setProfile] = useState<PartnerProfile>(); const [error, setError] = useState(''); const [busy, setBusy] = useState(true);
  const refresh = async () => { const session = await window.ANKT.auth.getSession(); setProfile(session.authenticated ? await window.ANKT.user.getProfile() : undefined); };
  useEffect(() => { let active = true; void initialize().then(async () => { if (active) await refresh(); }).catch(e => { if (active) setError(e.message); }).finally(() => { if (active) setBusy(false); }); return () => { active = false; }; }, []);
  const action = async (run: () => Promise<unknown>) => { setBusy(true); setError(''); try { await run(); await refresh(); } catch (e) { setError(e instanceof Error ? e.message : 'Không thể thực hiện yêu cầu.'); } finally { setBusy(false); } };
  return <main><p className="eyebrow">WEBSITE ĐỐI TÁC</p><h1>Dịch vụ của bạn</h1><p>Website hoạt động độc lập và kết nối tài khoản ANKT khi người dùng lựa chọn.</p>
    {busy && <p role="status">Đang kết nối…</p>}{error && <><p role="alert">{error}</p><button disabled={busy} onClick={() => void action(() => initialize())}>Thử lại kết nối</button></>}
    {profile ? <><h2>Xin chào {profile.name ?? 'bạn'}</h2><p>Phiên đăng nhập được quản lý bởi backend website này.</p><button disabled={busy} onClick={() => void action(() => window.ANKT.auth.logout())}>Đăng xuất website</button></> : <button disabled={busy || !window.ANKT} onClick={() => void action(() => window.ANKT.auth.login())}>Đăng nhập bằng ANKT</button>}
  </main>;
}
