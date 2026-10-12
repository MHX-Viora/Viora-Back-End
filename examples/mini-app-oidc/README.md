# ANKT OAuth/OIDC partner BFF

Hướng dẫn setup hai phía: [SETUP.md](../../docs/mini-app-partner/SETUP.md). Prompt để tích hợp vào website hiện có: [PROMPT-WEB-DOI-TAC.md](../../docs/mini-app-partner/PROMPT-WEB-DOI-TAC.md). Trong ANKT, mở `/developers/docs` → Chọn cách đăng nhập → ANKT SSO để xem hướng dẫn và sao chép prompt.

Ví dụ chạy được, dùng `openid-client` 6.8.8 thay cho API đổi code riêng của SDK 2.0. Backend giữ Client Secret, PKCE verifier, access token và session; trình duyệt chỉ nhận cookie HttpOnly và thông tin giao dịch công khai. SDK 2.2 hỗ trợ `ANKT.auth.autoLogin()` và `ANKT.auth.login()`.

## 1. Chuẩn bị trong Developer Console

1. Hồ sơ Developer được duyệt.
2. Mini App chọn đăng nhập ANKT, domain đã xác minh, callback đăng ký chính xác `https://<website>/auth/ankt/callback`.
3. Yêu cầu và được duyệt quyền `identity.login`, `profile.basic`; tương ứng scope `openid profile` của ví dụ. Muốn email hoặc số điện thoại phải xin và được duyệt thêm quyền tương ứng, đồng thời có xác nhận chia sẻ của người dùng.
4. Phiên bản được duyệt/phát hành. Lấy Client ID và Client Secret từ Portal.
   Bật **Tự động bắt đầu SSO khi mở Mini App** trong cấu hình, gửi duyệt và phát hành phiên bản đó. SSO Optional/Required không tự cấp quyền; người dùng phải đồng ý scope còn thiếu.
5. Nhà vận hành ANKT bật Open API và cung cấp issuer/host công khai hoạt động. Tên miền trong `.env.example` chỉ là mẫu cấu hình; ví dụ không tự tạo DNS hoặc deploy ANKT.

## 2. Chạy ví dụ

Node.js 22 trở lên:

```powershell
cd examples/mini-app-oidc
Copy-Item .env.example .env
# Sửa .env bằng giá trị của Mini App; không đưa .env vào git.
npm ci
npm test
npm start
```

Backend lắng nghe `127.0.0.1:4310`. Reverse proxy HTTPS của website cần chuyển `/auth/ankt/*`, `/`, `/client.mjs`, `/style.css` tới cổng này. Domain callback phải trùng domain HTTPS website đang mở. Với website hiện có, tích hợp các route BFF và JS vào frontend/backend của website; đừng thay toàn bộ website bằng trang demo.

`ANKT_OIDC_ISSUER` và `ANKT_HOST_ORIGIN` là địa chỉ công khai, không phải khóa. Có thể đưa vào package cấu hình do ANKT cung cấp; thư viện không mặc định trỏ vào API nội bộ. `ANKT_CLIENT_SECRET` luôn chỉ ở backend. Không đặt vào `VITE_*`, `NEXT_PUBLIC_*`, `EXPO_PUBLIC_*`.

## 3. Luồng đăng nhập

```text
Frontend trong ANKT -> configure BFF -> SDK ready -> auth.autoLogin()
SDK -> GET /auth/ankt/session -> cookie HttpOnly + CSRF
SDK -> kiểm tra session website có runtimeSessionId khớp READY
Nếu khớp -> GET profile, hiển thị nội dung đã đăng nhập
Nếu chưa khớp -> POST start {appId, runtimeSessionId} + CSRF
Backend  -> state + nonce + verifier, giữ giao dịch trong session
SDK -> requestOAuthLogin với giao dịch công khai
Host ANKT -> xác nhận chỉ khi cần chia sẻ thêm dữ liệu -> authorizeUrl
Frontend -> chuyển tới authorizeUrl
Open API -> callback website ?code=...&state=...
Backend  -> thư viện đổi code bằng secret + verifier; kiểm tra JWT/JWKS/nonce
Backend  -> runtime_session_id trong ID token phải khớp giao dịch đã lưu
Backend  -> tạo/tra tài khoản theo (issuer, sub); xoay cookie -> /
Frontend -> GET /auth/ankt/profile -> hồ sơ tối thiểu, không trả token
```

Frontend ví dụ cấu hình `ANKT.configure({hostOrigin, bff: true})`, gọi `await ANKT.auth.autoLogin()` khi có iframe/native bridge/opener. Hàm chờ handshake được xác minh, chỉ bắt đầu khi READY.autoLogin=true, giữ một lần thử mỗi document và không lặp khi bị từ chối. Sau callback, `/auth/ankt/session` trả `authenticated` và `runtimeSessionId` lấy từ ID token đã xác minh; SDK dùng lại phiên đúng runtime. Runtime mới luôn xác minh lại qua OAuth để không nhầm tài khoản ANKT; consent hợp lệ không hỏi lại. Website độc lập dùng `auth.login()` không tham số. `user.getProfile()`/`auth.logout()` gọi BFF cùng origin. `auth.login(sdkParams)` vẫn trả authorizeUrl cho tích hợp BFF tùy chỉnh.

Mở website bên ngoài ANKT: cùng BFF, SDK dùng `authorizeUrl` do `/auth/ankt/start` trả về; ANKT sẽ mở màn đăng nhập/xác nhận phù hợp. Không có token ANKT chính nào đi qua iframe. Nhấn mở Mini App không tự cấp quyền mới. Consent còn hiệu lực được tái sử dụng.

`POST /auth/ankt/logout` yêu cầu Origin + CSRF, xóa phiên website và gửi yêu cầu thu hồi access token. Không đăng xuất ANKT chính. Nếu provider tạm lỗi, phiên website vẫn bị xóa, response báo `providerRevoked:false`; UI thông báo token provider còn hiệu lực tới thời hạn đã cấp. Đăng xuất ANKT đóng runtime/cửa sổ; phiên website độc lập có thời hạn riêng, không giả định global logout.

## 4. Giới hạn triển khai cần hiểu

Ví dụ dùng Map trong RAM cho session và mapping tài khoản để chạy độc lập: khởi động lại làm mất dữ liệu, nhiều instance không chia sẻ phiên. Trước production, thay bằng Redis/session store và database của website, unique `(issuer, sub)`, consume giao dịch nguyên tử, giữ lifetime và xoay session sau login. Không tự liên kết tài khoản cũ theo email; người dùng phải đăng nhập tài khoản cũ rồi xác nhận liên kết.

Cookie production dùng `__Host-`, Secure, HttpOnly, SameSite=None và Partitioned. ANKT Web mở Mini App Auto Login trong cửa sổ đối tác từ thao tác chọn ứng dụng; SDK dùng opener cùng exact-origin/source binding, để cookie BFF là first-party. Không phụ thuộc iframe hoặc cookie ANKT. Khi popup bị chặn hoặc mở bằng deep link, người dùng cần nhấn Mở Mini App/cho phép cửa sổ. Website/issuer dùng COOP phải cho phép opener hoạt động theo chính sách đã kiểm tra, nếu opener bị cắt SDK sẽ không thể tự SSO. Native giữ WebView/cookie riêng, không chia sẻ cookie ANKT. `LOCAL_DEVELOPMENT=true` chỉ cho callback HTTP loopback; SDK host vẫn yêu cầu HTTPS. Không bật tùy chọn này trên website public.

Protected action gọi UserInfo trên backend để quyền/token bị thu hồi ngừng có hiệu lực ngay. Không chỉ tin cookie website cho thao tác cần dữ liệu ANKT. Không cache quyền lâu hơn yêu cầu bảo mật của thao tác.

## 5. Kiểm thử

`npm test`: HTTP CSRF, callback replay, session rotation, không trả token; đồng thời dùng thư viện thật với JWKS/JWT fixture để kiểm tra state, nonce, issuer, audience, expiry và chữ ký. Các test không chứng minh DNS/TLS/deploy thật. Smoke test môi trường của bạn: đăng nhập ngoài ANKT; tự tiếp tục trong ANKT; từ chối consent; thu hồi quyền; suspend app; cookie bị chặn; tài khoản ANKT bị khóa.

`auto-login.test.mjs` chạy SDK thật, BFF HTTP thật và openid-client với provider ký giả lập: xác nhận callback tạo phiên, profile được xác thực, cookie được xoay, cùng runtime không OAuth lại, đổi runtime/tài khoản không nhầm subject. `browser-fixture.mjs` chỉ dùng HTTPS localhost (4311/4312/4313), cần PFX test qua `ANKT_TEST_CERT_PATH` và mật khẩu test `ANKT_TEST_CERT_PASSWORD`. Không dùng thông tin production; không chạy fixture trên public interface. Fixture không chứng minh consent/backend ANKT production, phần đó được kiểm tra riêng bằng .NET TestServer.

Tham khảo chính thức: [openid-client](https://github.com/panva/openid-client), [authorizationCodeGrant](https://github.com/panva/openid-client/blob/main/docs/functions/authorizationCodeGrant.md), [signature verification](https://github.com/panva/openid-client/blob/main/docs/functions/enableNonRepudiationChecks.md).
