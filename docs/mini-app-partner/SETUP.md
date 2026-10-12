# Setup Mini App ANKT và website đối tác

Có hai lựa chọn. **IndependentWebsite**: website HTTPS dùng đăng nhập riêng hoặc không đăng nhập; khai báo website/domain/origin trong Portal, xác minh domain, gửi duyệt và phát hành. Không cần SDK/OAuth; không nhận danh tính ANKT. **AnktSso + Auto Login**: làm đầy đủ các bước bên dưới.

## 1. Cấu hình trên ANKT

Hồ sơ Developer phải được duyệt. Tạo Mini App, chọn Đăng nhập bằng ANKT SSO, chọn Optional hoặc Required và bật Tự động bắt đầu SSO khi mở Mini App. Website và callback dùng HTTPS; callback phải trùng chính xác https://<website>/auth/ankt/callback. Xác minh tất cả domain/origin, xin identity.login (scope openid); chỉ xin profile.basic (scope profile) nếu cần. Lưu Client ID, giữ Client Secret trên backend. Gửi xét duyệt và phát hành cấu hình này; sửa bản nháp chưa làm bản đang chạy thay đổi.

## 2. Nhận thông số kết nối

Lấy issuer Open API và host origin chính xác từ đơn vị vận hành ANKT hoặc thông tin tích hợp được cấp. Đây là hai địa chỉ khác nhau. Không đoán tên miền và không dùng API nội bộ ANKT. Nền tảng phải triển khai backend Open API, SDK 2.2 và host hỗ trợ Auto Login trước khi thử.

## 3. Setup backend website

Tạo backend đăng nhập cùng địa chỉ website theo contract trong phần dành cho lập trình viên. Callback phải xác minh kết quả ANKT rồi tạo phiên đăng nhập thật của website. Production cần database và nơi lưu phiên dùng chung; không dùng bộ nhớ RAM của demo để lưu tài khoản.

## 4. Setup frontend website

Tải SDK 2.2 từ issuer đã cấp và configure BFF. Trong runtime ANKT, luôn gọi auth.autoLogin() trước khi hiển thị nội dung theo tài khoản, kể cả khi website đã có cookie đăng nhập. SDK kiểm tra runtime hiện tại; consent còn hiệu lực được dùng lại. Khi mở website độc lập, dùng nút Đăng nhập bằng ANKT gọi auth.login(), hoặc giữ đăng nhập riêng.

## 5. Setup HTTPS và trình duyệt

Reverse proxy các route /auth/ankt/* về backend website, không trả trang SPA thay cho callback. Cho phép tải SDK trong CSP. ANKT Web mở cửa sổ đối tác từ thao tác người dùng; chính sách COOP phải giữ được opener để SDK handshake. Native dùng WebView riêng. Kiểm tra cookie Secure/HttpOnly và callback thực tế; không phụ thuộc cookie ANKT hoặc cookie bên thứ ba trong iframe.

## 6. Kiểm tra trước khi phát hành

Mở từ ANKT đã đăng nhập: lần đầu hỏi quyền còn thiếu, sau callback vào nội dung đã đăng nhập; mở lại không hỏi quyền cũ. Đổi tài khoản A sang B không hiện dữ liệu A. Thử từ chối quyền, callback/code dùng lại, state/nonce sai, runtime hết hạn, popup bị chặn và logout website. Lỗi phải có thông báo và nút thử lại, không tự chuyển hướng lặp.

## Biến môi trường backend

```dotenv
# Backend: thay toàn bộ giá trị <...> bằng thông tin thật
ANKT_OIDC_ISSUER=<issuer-Open-API-do-ANKT-cap>
ANKT_HOST_ORIGIN=<origin-HTTPS-cua-frontend-ANKT>
ANKT_CLIENT_ID=<client-id-cua-Mini-App>
ANKT_CLIENT_SECRET=<secret-chi-luu-tren-backend>
ANKT_REDIRECT_URI=https://<website>/auth/ankt/callback
# Chỉ cho demo Node; website khác dùng cổng backend hiện có
PORT=4310
LOCAL_DEVELOPMENT=false
```

## Contract BFF cùng origin website

### GET /auth/ankt/session

Trả { csrfToken, authenticated, runtimeSessionId, issuer, hostOrigin }; không trả token/secret. runtimeSessionId của phiên đã đăng nhập phải lấy từ ID token đã xác minh.

### POST /auth/ankt/start

Kiểm tra Origin + X-CSRF-Token. Nhận { appId, runtimeSessionId } khi chạy trong ANKT; tạo giao dịch một lần, có TTL. Trả { authorizeUrl, sdkParams: { clientId, redirectUri, state, nonce, codeChallenge, codeChallengeMethod: "S256", scopes } }. Không trả verifier.

### GET /auth/ankt/callback

Nhận code/state, consume giao dịch nguyên tử, đổi code trên backend bằng secret + verifier. Kiểm tra chữ ký/JWKS, issuer, audience, expiry, state, nonce và runtime_session_id khớp giao dịch trong ANKT. Tra/tạo tài khoản theo (issuer, sub), xoay session rồi redirect về trang ứng dụng.

### GET /auth/ankt/profile

Kiểm tra phiên website; trả hồ sơ tối thiểu, không trả access/ID token. Thao tác dùng dữ liệu ANKT cần kiểm tra quyền/token còn hiệu lực từ backend.

### POST /auth/ankt/logout

Kiểm tra Origin + CSRF, xóa phiên/cookie website và thử thu hồi token. Thu hồi lỗi vẫn xóa phiên cục bộ, trả providerRevoked:false; không tuyên bố đã đăng xuất ANKT chính.

## Điểm gọi SDK trên frontend

Đây là ví dụ nối SDK, cần triển khai render/error/login theo giao diện thật.

```javascript
// Sau khi tải <issuer>/mini-app-sdk/ankt-mini-app.js?v=2.2
ANKT.configure({ hostOrigin: HOST_ORIGIN_DO_ANKT_CAP, bff: true });
try {
  const inAnkt = window.parent !== window ||
    !!window.ReactNativeWebView || !!window.opener;
  if (inAnkt) {
    const result = await ANKT.auth.autoLogin();
    if (result.status === 'authenticated') {
      const profile = await ANKT.user.getProfile();
      // Render nội dung theo phiên website đã xác minh.
    } else if (result.status === 'disabled') {
      // Hiện nút đăng nhập chủ động: ANKT.auth.login().
    }
  } else {
    // Website độc lập: giữ login riêng hoặc nút ANKT.auth.login().
  }
} catch (error) {
  // Hiện lỗi + nút thử lại; không tự gọi lại vô hạn.
}
```

## Chạy demo tham chiếu

Trong bộ nguồn ANKT, dùng Node.js 22 trở lên:

```powershell
cd viora-BE/examples/mini-app-oidc
Copy-Item .env.example .env
# Sửa .env bằng metadata thật, không commit secret.
npm ci
npm test
npm start
```

Demo nghe 127.0.0.1:4310; đặt reverse proxy HTTPS với callback đã đăng ký. Xem [README của demo](../../examples/mini-app-oidc/README.md). Tích hợp server.mjs/oidc.mjs/client.mjs vào kiến trúc website hiện có; production thay Map RAM bằng database/session store dùng chung.

## Prompt cho lập trình viên

Dùng [PROMPT-WEB-DOI-TAC.md](./PROMPT-WEB-DOI-TAC.md). Trang /developer/guide trong ANKT cũng hiển thị prompt có thể chọn và sao chép ở bước Chọn cách đăng nhập → Đăng nhập bằng ANKT SSO.

## Chẩn đoán nhanh

| Triệu chứng | Kiểm tra |
|---|---|
| Auto Login không bắt đầu | SDK 2.2, flag ở phiên bản đã phát hành, handshake đúng host, popup/opener/COOP |
| Callback 404 hoặc trả HTML SPA | Route callback backend và reverse proxy; exact redirect URI đã đăng ký |
| invalid_client/invalid_scope | Client ID/secret thật của app và scope đã duyệt; scope demo openid profile cần cả hai quyền |
| csrf_failed hoặc callback mất giao dịch | Cookie cùng origin, Origin/header CSRF, HTTPS, TTL và session store |
| Đổi ANKT A sang B vẫn hiện A | Luôn autoLogin trong runtime; signed runtime_session_id; khóa phiên cũ trước callback mới |
| Sửa Portal nhưng app không đổi | Cấu hình bản nháp phải gửi duyệt và phát hành lại |

Tài liệu mô tả contract trong workspace. Không xác nhận ANKT hoặc website đối tác đã triển khai production; issuer/host mẫu không chứng minh dịch vụ đang hoạt động.
