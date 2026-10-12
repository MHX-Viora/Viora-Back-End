const status = document.querySelector('#status');
const loginButton = document.querySelector('#login');
const logoutButton = document.querySelector('#logout');
const profile = document.querySelector('#profile');
let metadata;
let loggingIn = false;
let sdkPromise;

async function session() {
  const response = await fetch('/auth/ankt/session', { credentials: 'same-origin' });
  if (!response.ok) throw new Error('Không thể kiểm tra phiên website.');
  metadata = await response.json();
  return metadata;
}
async function loadSdk(embedded) {
  sdkPromise ??= (async () => {
    if (!globalThis.ANKT) {
      const script = document.createElement('script');
      script.src = `${metadata.issuer}/mini-app-sdk/ankt-mini-app.js?v=2.2`;
      script.referrerPolicy = 'no-referrer';
      await new Promise((resolve, reject) => { script.onload = resolve; script.onerror = reject; document.head.append(script); });
    }
    globalThis.ANKT.configure({ hostOrigin: metadata.hostOrigin, bff: true });
    return globalThis.ANKT;
  })().catch(error => { sdkPromise = undefined; throw error; });
  const sdk = await sdkPromise;
  if (embedded) await sdk.ready();
  return sdk;
}
async function login(embedded) {
  if (loggingIn) return;
  loggingIn = true; loginButton.disabled = true; status.textContent = embedded ? 'Đang xác thực ANKT. Nếu được hỏi quyền, hãy xác nhận trong cửa sổ ANKT.' : 'Đang đăng nhập…';
  try {
    // Establish a trusted bridge BEFORE creating the backend transaction. If
    // third-party cookies/embedding are blocked, fail visibly; never loop.
    const sdk = await loadSdk(embedded);
    // SDK bootstraps the local BFF, then uses the host bridge when embedded or
    // standard authorization when standalone. No tokens pass through the SDK.
    if (embedded) {
      const result = await sdk.auth.autoLogin();
      if (result.status === 'authenticated') {
        profile.textContent = JSON.stringify(await sdk.user.getProfile(), null, 2);
        status.textContent = 'Đã đăng nhập bằng ANKT.'; logoutButton.hidden = false;
        loggingIn = false; loginButton.disabled = false;
      } else if (result.status === 'disabled') {
        status.textContent = 'Mini App chưa bật tự động đăng nhập. Có thể đăng nhập theo lựa chọn của bạn.';
        loginButton.hidden = false; loginButton.disabled = false; loggingIn = false;
      }
    } else await sdk.auth.login();
  } catch {
    status.textContent = 'Không thể tiếp tục đăng nhập. Nếu trình duyệt chặn cookie trong iframe, hãy mở website ở tab riêng.';
    loginButton.hidden = false; loginButton.disabled = false; loggingIn = false;
  }
}
loginButton.addEventListener('click', () => login(false));
logoutButton.addEventListener('click', async () => {
  try { const sdk = await loadSdk(false); const result = await sdk.auth.logout(); profile.textContent = ''; logoutButton.hidden = true; loginButton.hidden = false; loginButton.disabled = false; loggingIn = false; await session(); status.textContent = result.providerRevoked === false ? 'Đã đăng xuất website. Token tại ANKT chưa thu hồi được và sẽ hết hạn theo thời hạn đã cấp.' : 'Đã đăng xuất website.'; }
  catch { status.textContent = 'Không thể đăng xuất. Vui lòng tải lại.'; }
});
try {
  const current = await session();
  if (window.parent !== window || globalThis.ReactNativeWebView || window.opener) {
    // Even an authenticated cookie must match this host runtime before displaying
    // protected content. Switching ANKT accounts creates a new runtime session.
    await login(true);
  } else if (current.authenticated) {
    const sdk = await loadSdk(false);
    profile.textContent = JSON.stringify(await sdk.user.getProfile(), null, 2);
    status.textContent = 'Đã đăng nhập bằng ANKT.'; logoutButton.hidden = false;
  } else {
    status.textContent = 'Website đang mở độc lập.'; loginButton.hidden = false;
  }
} catch { status.textContent = 'Không thể đăng nhập. Hãy tải lại trang để thử lại.'; loginButton.hidden = false; }
