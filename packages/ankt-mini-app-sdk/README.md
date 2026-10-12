# @ankt/mini-app-sdk

TypeScript module đóng gói từ SDK runtime chính của BE; build không duy trì bản bridge riêng. Chạy `npm ci`, `npm run build`, `npm pack` để tạo tarball dùng nội bộ. Chưa publish npm registry.

```ts
import ANKT from '@ankt/mini-app-sdk';
await ANKT.initialize({ hostOrigin: 'https://<ankt-host>', bff: true });
const environment = ANKT.getEnvironment();
if (environment.embedded) await ANKT.auth.autoLogin();
const session = await ANKT.auth.getSession();
if (session.authenticated) console.log(await ANKT.user.getBasicProfile());
```

Chỉ import trong browser/WebView. Framework SSR dùng dynamic import trong client effect. `embedded/ready` chỉ đúng sau handshake được kiểm tra origin/source; không dùng metadata chưa handshake làm bằng chứng danh tính. Browser ngoài ANKT dùng `auth.login()` với BFF. Client secret/verifier/token nằm ở BFF, không nằm trong package hay cấu hình browser.

API nhóm: `initialize`, `getEnvironment`, `auth.login/getSession/logout`, `user.getBasicProfile`, `permissions.request`, `app.close`, `navigation.openExternal`. Các tên SDK 2.2 cũ giữ tương thích; `requestLogin` protocol riêng đã ngừng hỗ trợ, dùng `requestOAuthLogin` hoặc BFF `auth.login`.

[Hướng dẫn BFF và HTTPS](../../docs/mini-app-partner/SETUP.md).
