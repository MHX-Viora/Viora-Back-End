# ASP.NET OIDC BFF cho website đối tác

Dùng middleware `Microsoft.AspNetCore.Authentication.OpenIdConnect` cho Authorization Code + PKCE S256, state/correlation/nonce và JWT/JWKS validation. Cookie HttpOnly chỉ chứa mã vé; token nằm trong `ITicketStore` ở server. Mapping tài khoản SQLite có khóa duy nhất `(issuer, sub)`, không ghép tài khoản theo email.

```powershell
$env:ANKT__Issuer='https://<issuer-do-ANKT-cap>/'
$env:ANKT__ClientId='<client-id>'
$env:ANKT__ClientSecret='<secret-chi-tren-server>'
# Client public/None: bỏ biến secret.
$env:ConnectionStrings__Partner='Data Source=partner-oidc.sqlite'
dotnet build
dotnet run --urls https://localhost:4314
```

Đăng ký callback chính xác `https://<website>/auth/ankt/callback`, domain/origin HTTPS đã xác minh và quyền `identity.login`, `profile.basic`; gửi duyệt và phát hành cấu hình. Dev certificate chỉ dùng local. Với domain thật, cấu hình HTTPS/reverse proxy và forwarding headers chỉ từ proxy tin cậy để middleware tạo đúng callback HTTPS.

Ghép [frontend React](../mini-app-react/README.md) hoặc SDK vào website. Proxy `/auth/ankt/*` cùng origin tới BFF. `/session` cấp CSRF và trạng thái phiên; POST `/start` tạo giao dịch OIDC do middleware bảo vệ; callback kiểm tra `runtime_session_id` nếu bắt đầu trong ANKT. `/profile` yêu cầu phiên và kiểm tra token với issuer; `/logout` xóa vé/cookie của website, không đăng xuất ANKT chính.

`ServerTickets` là store RAM giới hạn 10.000 phiên, 30 phút, dành cho demo. Production phải thay bằng store chia sẻ được mã hóa, lưu Data Protection keys bền vững, dùng database website và migrations, cấu hình rate limit/body limit/proxy phù hợp. Restart demo làm mất phiên nhưng giữ mapping SQLite. Logout ví dụ này xóa phiên website; thu hồi quyền ANKT ở mục Ứng dụng đã kết nối, hoặc bổ sung endpoint revocation của issuer ở backend khi cần thu hồi token ngay.

Không gửi token/secret/verifier trong JSON, SDK hoặc biến frontend. Không dùng ví dụ exchange tùy chỉnh cũ. Hướng dẫn chung: [SETUP.md](../../docs/mini-app-partner/SETUP.md).
