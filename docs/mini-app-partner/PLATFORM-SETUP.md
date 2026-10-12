# Vận hành ANKT Mini App Platform

Mini App nằm trong **Tiện ích, ngay dưới Ví**. Developer Console sử dụng tài khoản và frontend ANKT hiện có; Admin duyệt hồ sơ/phiên bản và quản lý danh mục, banner, vị trí nổi bật. Không cần tạo frontend hay database ANKT thứ hai.

## Database và migrations

`AppDbContext` chứa social, ví và bảng Mini App. `OpenApiDbContext` chứa OpenIddict, dùng **cùng** `ConnectionStrings:DefaultConnection`, nhưng history riêng `__OpenApiMigrationsHistory`. Tên context không phải `ApplicationDbContext`.

```powershell
dotnet ef migrations list --project .\Viora.Infrastructure --startup-project .\viora-BE --context AppDbContext
dotnet ef migrations list --project .\Viora.Infrastructure --startup-project .\viora-BE --context OpenApiDbContext
```

Nếu DB đã chạy `RemoveRetiredPartnerPlatformTables`, áp dụng lần lượt hai migration mới `20261012072618_ReintroduceMiniAppPlatform` và `20261012072934_AddMiniAppMediaAndDiscoveryIndexes`, rồi migration protocol `20261010082214_AddOpenApiProtocolStore` trong history riêng. Bảng cũ đã xóa không tự phục hồi dữ liệu. Migration mới không xóa bảng social/ví. Không chạy rollback xuống migration retirement vì Down của migration xóa bảng cũ không hỗ trợ phục hồi.

SQL để review nằm ở `../deployment/mini-app-platform.sql` và `../deployment/mini-app-open-api.sql`. Sao lưu và kiểm tra history thực tế trước khi operator áp dụng. Các SQL này dành cho DB đã retirement; DB đang ở trạng thái khác cần tính lại điểm bắt đầu. Không tự chạy lại đoạn retirement trên DB đang có dữ liệu platform mới.

## Cấu hình BE

Issuer và API nội bộ dùng **hostname riêng**, có thể trỏ về cùng process và cùng DB. Boundary middleware chỉ cho route protocol/resource/SDK trên hostname issuer, chặn API social/ví/admin; hostname nội bộ chặn protocol. Frontend ANKT dùng API nội bộ cho phiên chính và issuer cho OIDC.

```dotenv
OpenApi__Enabled=true
OpenApi__Issuer=https://<public-issuer>/
OpenApi__Audience=ankt-open-api
OpenApi__AuthorizationUi=https://<ankt-frontend>/developers/console/authorize
OpenApi__KnownProxies__0=<IP-reverse-proxy-tin-cay>
OpenApi__SigningCertificatePath=<absolute-path-signing.pfx>
OpenApi__EncryptionCertificatePath=<absolute-path-encryption.pfx>
OpenApi__CertificatePassword=<server-secret>
```

Chứng thư production phải bền vững, private key ngoài Git, có kế hoạch rotation/backup. Development dùng dev certificate. CORS cho chính xác origin frontend/Admin. Reverse proxy giữ Host đúng hostname issuer/API và chuyển proto từ proxy đã đăng ký. HTTPS discovery `/.well-known/openid-configuration`, JWKS `/oauth/jwks`, token `/oauth/token`, userinfo `/oidc/userinfo`; SDK `/mini-app-sdk/ankt-mini-app.js` và declaration `.d.ts`.

Xác minh email Developer cần `DeveloperVerification__OtpHashKey` riêng (ít nhất 32 byte), `SmtpHost`, `SmtpPort`, `SmtpUsername`, `SmtpPassword`, `SenderEmail` với cùng prefix `DeveloperVerification__`. Mẫu không chứa secret: `deploy/open-api/developer-platform.env.example`. Chưa cấu hình SMTP thì API trả lỗi delivery; không trả OTP để bỏ qua xác minh.

## Frontend và Admin

```dotenv
# ANKT Expo build: metadata công khai, không có secret
EXPO_PUBLIC_API_URL=https://<internal-api-host>
EXPO_PUBLIC_OPEN_API_ISSUER=https://<public-issuer>
# Admin build
VITE_API_BASE_URL=https://<internal-api-host>
```

Build FE `npm run build:web`, Admin `npm run build`; giữ các rule static public docs/Nginx hiện có, route authorize phải đi vào SPA và đăng nhập ANKT. Native phải rebuild development/production client sau khi thêm `react-native-webview` và `expo-crypto`.

## Quy trình phát hành

Developer lưu hồ sơ, xác minh email, chấp nhận điều khoản, gửi duyệt. Admin xét duyệt hồ sơ. Developer tạo app, xác minh domain, cấu hình callbacks/origins/quyền, chạy kiểm tra kỹ thuật, gửi phiên bản. Admin xét duyệt snapshot; Developer phát hành phiên bản đã duyệt. Catalog chỉ đọc snapshot đã phát hành; sửa draft không thay app đang chạy. Admin có thể ẩn catalog riêng hoặc suspend app/developer để chặn truy cập và cấp token.

Kiểm tra môi trường HTTPS thực: domain proof, email gửi thật, consent deny/approve, callback replay, đổi tài khoản, revoke, suspend, cookie bị chặn và fallback web. Kiểm tra WebView Android/iOS trên thiết bị thật trước khi phát hành native. Build/test local không chứng minh cấu hình DNS/TLS/email/native hay production đã triển khai.

Hướng dẫn đối tác và ví dụ: [SETUP.md](SETUP.md), [Node BFF](../../examples/mini-app-oidc/README.md), [React](../../examples/mini-app-react/README.md), [ASP.NET BFF](../../examples/mini-app-oidc-dotnet/README.md).
