# Prompt triển khai Mini App ANKT trên website đối tác

Sao chép phần bên dưới vào công cụ lập trình trong repository website đối tác. Thay placeholder bằng metadata được ANKT cấp; không dán Client Secret vào prompt.

Hãy tích hợp website hiện có thành Mini App ANKT có Auto Login, giữ nguyên giao diện, nghiệp vụ và cách đăng nhập riêng.

Đầu vào do tôi cung cấp:
- Website HTTPS và trang mở Mini App: <URL_WEBSITE>
- Issuer Open API ANKT: <ANKT_OIDC_ISSUER>
- Origin frontend ANKT: <ANKT_HOST_ORIGIN>
- Client ID: <ANKT_CLIENT_ID>
- Callback chính xác: <ANKT_REDIRECT_URI>
- Client Secret được cấu hình trong secret manager/backend, không dán vào prompt hoặc commit.
- Quyền đã duyệt: identity.login/openid; profile.basic/profile chỉ khi thực sự cần.
Nếu thiếu metadata thật, tạo .env.example bằng placeholder và ghi rõ bước cần cấu hình, không tự bịa endpoint hoặc kết quả tích hợp.

1. Đọc repository, xác định frontend, backend, database, cơ chế session hiện tại. Lập kế hoạch tối thiểu và triển khai trong kiến trúc hiện có. Nếu chỉ có frontend tĩnh, bổ sung BFF cùng origin; không đặt secret hoặc token vào frontend.

2. Dùng thư viện OAuth/OIDC phù hợp stack và tài liệu chính thức. Khám phá endpoint từ issuer; Authorization Code + PKCE S256. Backend giữ client secret, verifier, state, nonce, access/ID token. Không dùng API đổi code cũ POST /api/mini-app-auth/exchange, không gọi API nội bộ ANKT.

3. Cài BFF cùng origin website:
GET /auth/ankt/session -> { csrfToken, authenticated, runtimeSessionId, issuer, hostOrigin }.
POST /auth/ankt/start -> kiểm tra Origin/CSRF; nhận {appId,runtimeSessionId} khi có runtime, tạo state/nonce/verifier với TTL và lưu server-side; trả {authorizeUrl,sdkParams:{clientId,redirectUri,state,nonce,codeChallenge,codeChallengeMethod:'S256',scopes}}.
GET /auth/ankt/callback -> consume giao dịch nguyên tử một lần, kiểm tra state, đổi code qua token endpoint trên backend bằng secret/verifier; xác minh JWKS/chữ ký/issuer/audience/expiry/nonce. Trong runtime ANKT, claim runtime_session_id đã ký phải khớp giao dịch; không coi ID từ browser là bằng chứng danh tính. Tạo phiên website thật, xoay cookie, redirect về trang ứng dụng.
GET /auth/ankt/profile -> hồ sơ tối thiểu của phiên, không trả token.
POST /auth/ankt/logout -> kiểm tra Origin/CSRF, xóa phiên cục bộ, thử revoke; báo providerRevoked:false nếu thất bại. Không logout ANKT chính.

4. Tải SDK 2.2 từ <issuer>/mini-app-sdk/ankt-mini-app.js?v=2.2; ANKT.configure({hostOrigin:<origin được cấp>,bff:true}). Khi có iframe/native bridge/opener, gọi await ANKT.auth.autoLogin() trước khi render nội dung tài khoản, kể cả khi đã có cookie website. Chỉ render hồ sơ khi result.status==='authenticated'; với disabled cho phép login chủ động; lỗi có thông báo/thử lại, không retry hoặc redirect vô hạn. Website độc lập giữ login cũ hoặc nút await ANKT.auth.login(). SDK tự tạo giao dịch qua BFF và kết nối host; không tự gửi token ANKT qua postMessage.

5. Lưu mapping unique (issuer, sub) vào database. Không tự gộp tài khoản theo email. Khi runtime đổi, ngừng phục vụ dữ liệu của phiên ANKT cũ trước khi xác thực tài khoản mới; nếu callback thất bại không được hiện dữ liệu cũ. Liên kết tài khoản cũ chỉ sau khi người dùng xác thực tài khoản đó và xác nhận. Dùng session store dùng chung cho nhiều instance, TTL, giao dịch một lần, cookie Secure/HttpOnly phù hợp trình duyệt; không copy Map RAM của demo vào production. Các thao tác dùng quyền ANKT phải kiểm tra token/quyền còn hiệu lực trên backend.

6. Viết hướng dẫn cấu hình .env/secret manager, HTTPS/reverse proxy, callback không bị SPA fallback, CSP tải SDK và COOP giữ opener. ANKT Web dùng cửa sổ đối tác với cookie first-party; không yêu cầu cookie ANKT hay phụ thuộc cookie bên thứ ba trong iframe. Không vô hiệu xác minh TLS/JWT.

7. Viết checklist Portal: hồ sơ Developer được duyệt; AnktSso Optional/Required; bật Auto Login; exact callback; xác minh tất cả domain/origin; quyền tối thiểu; gửi duyệt và phát hành. Chỉ sửa bản nháp không bật Auto Login cho bản người dùng đang chạy. ANKT phải triển khai Open API/SDK/host hỗ trợ trước.

8. Kiểm thử: callback tạo phiên đăng nhập thật; consent cũ dùng lại/quyền mới được hỏi; A đổi sang B không lẫn dữ liệu; cookie cũ khác runtime không bỏ qua OAuth; từ chối consent; state/nonce/JWT/PKCE sai; code replay/hết hạn; CSRF; popup/opener bị chặn; logout/revoke lỗi; website độc lập. Kiểm tra không có secret/token trong bundle, URL ứng dụng, localStorage, postMessage hoặc response BFF. URL callback chỉ mang code/state theo OAuth, không mang access token.

Bàn giao: code hoàn chỉnh, migration mapping/session nếu cần, .env.example không chứa secret, hướng dẫn setup hai phía, lệnh test/build và kết quả thật. Phân biệt kiểm thử fixture với ANKT thật; ghi rõ hạng mục chưa kiểm chứng. Không deploy production khi chưa được yêu cầu.

Nếu có bộ nguồn ANKT, đối chiếu ví dụ viora-BE/examples/mini-app-oidc (server.mjs, oidc.mjs, client.mjs và README.md). Nếu không có, yêu cầu tài liệu/bộ ví dụ chính thức để đối chiếu contract trước khi tích hợp thật.
