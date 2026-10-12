# Tích hợp partner: JavaScript, React, ASP.NET Core 8

## Contract chung

Discovery tại `<ANKT_OIDC_ISSUER>/.well-known/openid-configuration`. Sử dụng endpoint discovery trả về, không gọi API nội bộ. Code flow + S256 PKCE, callback đăng ký tuyệt đối chính xác, state/nonce từng giao dịch. `openid` luôn bắt buộc; thêm `profile`, `email`, `phone` chỉ khi quyền đã được duyệt và người dùng đồng ý. Không có `offline_access`/refresh token trong bản này.

Bridge SDK chỉ chạy bên trong host đã cấu hình origin HTTPS chính xác. `ANKT.auth.login(params)` trả `{ authorizeUrl, expiresIn }`; chuyển trang tới URL này để OpenID server tạo code callback chuẩn. `configure({hostOrigin, bff: true})` + `ANKT.auth.login()` không tham số tự tạo giao dịch qua BFF cùng origin rồi điều hướng đúng luồng embedded/website độc lập. Secrets, verifier, access token và ID token chỉ nằm backend. Host ANKT không đưa JWT chính sang website.

## JavaScript backend + browser chạy được

Dùng [examples/mini-app-oidc](../examples/mini-app-oidc/README.md). Đây là BFF dùng thư viện `openid-client`, có frontend tự tiếp tục khi nằm trong ANKT; đổi code, kiểm tra ID token, fetch UserInfo và revoke token thật. SDK 2.0 `requestLogin()` vẫn là flow legacy riêng; không gửi code legacy vào `/oauth/token`.

## React trên BFF cùng origin

Giữ route BFF của ví dụ Node. Đừng dùng `useEffect` tự đăng nhập hai lần do React Strict Mode. Để điều phối tự đăng nhập, chia sẻ Promise ở module và reset khi lỗi; không gắn code verifier/token vào state hoặc storage của React.

```tsx
import { useEffect, useState } from 'react';

let loginInProgress: Promise<void> | undefined;
async function beginLogin(embedded: boolean) {
  if (loginInProgress) return loginInProgress;
  loginInProgress = (async () => {
    const sessionResponse = await fetch('/auth/ankt/session', { credentials: 'same-origin' });
    if (!sessionResponse.ok) throw new Error('session_failed');
    const metadata = await sessionResponse.json();
    if (embedded) {
      // Load SDK from metadata.issuer + '/mini-app-sdk/ankt-mini-app.js' once
      // in your application entry. Issuer must come from your configured BFF.
      window.ANKT.configure({ hostOrigin: metadata.hostOrigin });
      await window.ANKT.ready();
    }
    const response = await fetch('/auth/ankt/start', {
      method: 'POST', credentials: 'same-origin',
      headers: { 'X-CSRF-Token': metadata.csrfToken },
    });
    if (!response.ok) throw new Error('start_failed');
    const transaction = await response.json();
    const result = embedded ? await window.ANKT.auth.login(transaction.sdkParams) : transaction;
    const url = new URL(result.authorizeUrl);
    if (url.origin !== metadata.issuer || url.pathname !== '/oauth/authorize') throw new Error('invalid_authorize_url');
    window.location.assign(url.href);
  })();
  try { await loginInProgress; } catch (error) { loginInProgress = undefined; throw error; }
}

export function AnktLogin() {
  const [error, setError] = useState('');
  useEffect(() => {
    let mounted = true;
    (async () => {
      const response = await fetch('/auth/ankt/session', { credentials: 'same-origin' });
      if (!response.ok) throw new Error('session_failed');
      const session = await response.json();
      if (!mounted || session.authenticated) return;
      if (window.parent !== window || window.ReactNativeWebView) await beginLogin(true);
    })().catch(() => { if (mounted) setError('Không thể đăng nhập. Hãy thử mở website ở tab riêng.'); });
    return () => { mounted = false; };
  }, []);
  return <><p role="status">{error}</p><button onClick={() => {
    beginLogin(false).catch(() => setError('Không thể đăng nhập. Vui lòng tải lại.'));
  }}>Đăng nhập bằng ANKT</button></>;
}
```

Khai báo kiểu `window.ANKT` theo interface SDK trong dự án của bạn. Cách gọn hơn sau khi tải SDK:

```js
ANKT.configure({ hostOrigin: metadata.hostOrigin, bff: true });
// Embedded: await ANKT.ready(); Website độc lập: không chờ handshake.
await ANKT.auth.login();       // SDK điều hướng đăng nhập qua BFF cùng origin.
const user = await ANKT.user.getProfile(); // Phiên website; không trả token.
await ANKT.auth.logout();      // CSRF bootstrap + logout/revoke trên BFF.
```

Các lời gọi này thực hiện tại thời điểm phù hợp: login khi chưa có phiên; profile sau callback; logout khi người dùng chọn. Có thể đặt `bff: {sessionPath, startPath, profilePath, logoutPath}` để đổi route cùng origin; SDK từ chối path sang domain khác. `ANKT.auth.getPermissions()` dùng bridge nên chỉ gọi trong ANKT sau ready; website độc lập lấy scope được cấp từ backend lưu token response, không dùng bridge. `/auth/ankt/logout` xóa phiên website và revoke token, phải kèm CSRF. Không thay logout bằng xóa localStorage.

## ASP.NET Core 8: standard OIDC handler

Dùng `Microsoft.AspNetCore.Authentication.OpenIdConnect` 8.x và Cookie handler, không tự viết JWT verifier. Cấu hình này dành cho luồng website độc lập. Nếu muốn SDK tự đăng nhập trong Mini App, cần thêm BFF start route cung cấp cùng giao dịch mà OIDC handler tạo (đã bảo vệ state, correlation cookie, nonce và verifier), thay vì tạo một giao dịch JavaScript khác.

```csharp
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

builder.Services.AddAuthentication(options => {
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = OpenIdConnectDefaults.AuthenticationScheme;
})
.AddCookie(options => {
    options.Cookie.Name = "__Host-partner-session";
    options.Cookie.HttpOnly = true;
    options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
    options.Cookie.SameSite = SameSiteMode.None;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(5);
    options.SlidingExpiration = false;
    // Configure SessionStore = your Redis-backed ITicketStore via DI.
    // SaveTokens below puts tokens into that server-side ticket only.
})
.AddOpenIdConnect(options => {
    options.Authority = builder.Configuration["ANKT_OIDC_ISSUER"]
        ?? throw new InvalidOperationException("ANKT_OIDC_ISSUER missing");
    options.ClientId = builder.Configuration["ANKT_CLIENT_ID"]
        ?? throw new InvalidOperationException("ANKT_CLIENT_ID missing");
    options.ClientSecret = builder.Configuration["ANKT_CLIENT_SECRET"]
        ?? throw new InvalidOperationException("ANKT_CLIENT_SECRET missing");
    options.CallbackPath = "/auth/ankt/callback";
    options.ResponseType = OpenIdConnectResponseType.Code;
    options.ResponseMode = OpenIdConnectResponseMode.Query;
    options.UsePkce = true;
    options.RequireHttpsMetadata = true;
    options.MapInboundClaims = false;
    options.Scope.Clear();
    options.Scope.Add("openid");
    options.Scope.Add("profile");
    options.SaveTokens = false; // Leave false until a server-side ITicketStore exists.
    options.GetClaimsFromUserInfoEndpoint = true;
    options.TokenValidationParameters.NameClaimType = "name";
    options.TokenValidationParameters.ValidAlgorithms = ["RS256"];
    options.Events.OnRemoteFailure = context => {
        context.HandleResponse();
        context.Response.Redirect("/login?error=ankt_login_failed");
        return Task.CompletedTask;
    };
});
builder.Services.AddAuthorization();
// After Build: trusted forwarding/HTTPS, routing, authentication, authorization.
app.UseAuthentication();
app.UseAuthorization();
```

Framework tự kiểm tra signature/JWKS, issuer, audience, expiry, nonce và state; tự giữ PKCE verifier trong authentication properties được bảo vệ. Callback phải là URL HTTPS hoàn chỉnh đã đăng ký, được tạo theo host public cố định và proxy đáng tin, không lấy tùy ý từ request Host. Bảo vệ nút bắt đầu và logout bằng antiforgery POST; chỉ redirect về route nội bộ cố định sau thành công. Không gọi endpoint logout OIDC nếu discovery không công bố endpoint đó: bản ANKT này hỗ trợ revoke access token và logout session website, không sign-out tài khoản ANKT chính.

Để gọi API sau đăng nhập, bổ sung ITicketStore trên backend rồi bật SaveTokens, giữ token lifetime tối đa 5 phút, gọi UserInfo/Open API trên backend ở từng protected action và bỏ cookie khi nhận 401/revocation. Cookie có thể thêm `Partitioned` theo hỗ trợ runtime/browser. Tạo mapping DB unique `(issuer, sub)`; linking tài khoản cũ cần xác nhận từ người đã đăng nhập, không khớp email tự động.

Tham khảo: [Microsoft OIDC handler](https://learn.microsoft.com/en-us/aspnet/core/security/authentication/configure-oidc-web-authentication?view=aspnetcore-8.0), [openid-client API](https://github.com/panva/openid-client/blob/main/docs/README.md).

## Kiểm tra trước khi thay legacy integration

Giữ nút đăng nhập cũ trong giai đoạn chuyển đổi, bật mới theo feature flag của website. Xác minh issuer discovery/JWKS, callback đúng, consent được tái sử dụng, quyền mới vẫn hỏi, code replay bị chặn, revoke/suspend/khóa user chặn resource ngay, browser không nhận token/secret. Không xóa endpoint legacy trước khi toàn bộ partner đã chuyển và quan sát ổn định.
