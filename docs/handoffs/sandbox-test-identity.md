# Báo cáo Sandbox Test Identity — 24/10/2026

## Kết quả

Đã triển khai và kiểm thử local luồng Developer Portal → tài khoản Sandbox →
OpenIddict authorization code + PKCE → callback Mini App → UserInfo → phiên đối
tác. Luồng chính không cần hostname Sandbox riêng. Chưa deploy Render hoặc chạy
migration trên database ứng dụng thật.

## Lưu tài khoản và định danh

- Template có bản ghi thật trong `Accounts` và `Users`, email
  `sandbox@example.test`, không password, không xác minh thật.
- Mỗi cặp Developer–Mini App có Account/User riêng với `IsSandbox=true` và email
  nội bộ duy nhất để giữ unique index hiện tại. Profile công khai dùng mẫu chung.
- `SandboxTestIdentities` giữ mapping; cơ chế `MiniAppPairwiseSubjects` hiện có
  giữ `sub` ổn định. Không dùng Account ID production làm subject chung.
- Tài khoản test bị loại khỏi truy vấn Account/User thông thường và không được
  cấp JWT/refresh token tài khoản Production. Không tạo ví, thanh toán hoặc gửi
  thông báo/email cho người dùng thật.
- Không tạo hồ sơ KYC giả. Full name/nickname/birthdate bổ sung nằm trong template;
  avatar, cover, display name, gender dùng trường User đang có.

## SSO và API

Owner APIs mới, dưới `/api/developer/mini-apps/{appId}`:

| API | Hành vi |
| --- | --- |
| `GET /sandbox-test` | Profile mẫu, readiness SSO, callback và phiên gần đây |
| `POST /sandbox-test/sessions`, body `{}` | Kiểm tra quyền; tạo ticket dùng một lần, hạn 60 giây; phiên tối đa 15 phút |
| `DELETE /sandbox-test/sessions/{id}` | Thu hồi đúng phiên thuộc owner/app |

Browser POST ticket tới `/sso/sandbox-test/start` trên issuer. Endpoint kiểm tra
HTTPS/Host/Origin, consume ticket atomically và đặt cookie Secure/HttpOnly/Lax
riêng theo OAuth client. Credentials chỉ lưu hash trong DB; không truyền access
token, refresh token, password hoặc client secret trong URL.

Reuse `/connect/authorize`, `/connect/token`, `/connect/userinfo`, consent,
antiforgery và signing/encryption hiện có. Consent đầu tiên vẫn bắt buộc. Mỗi
exchange/UserInfo kiểm tra lại source Developer session, identity, app, domain,
client/version, expiry và revocation. Token lifetime tôn trọng `Sso:AccessTokenSeconds`,
kể cả giá trị ngắn hơn 300 giây. Tắt tính năng không bật fallback tài khoản thật
cho client Sandbox.

Claim theo scope:

- `openid`: `sub`.
- `profile`: `name`, `preferred_username` (display name), `nickname`, `gender`,
  `birthdate`, `picture`, `ankt_cover_picture`.
- `email`: `email`, `email_verified=false` cho mẫu.

Production UserInfo giữ chính sách dữ liệu đang có. Source/session nội bộ không
được thêm vào UserInfo hoặc ID token. Các field mới của mẫu không tự mở dữ liệu
KYC/profile thật cho ứng dụng chưa duyệt.

## Cloudinary

Đã thực sự provision hai SVG không dùng ảnh cá nhân, với public IDs cố định:

- `ankt/sandbox/avatar-v1`
- `ankt/sandbox/cover-v1`

Hai URL HTTPS versioned được lưu vào User của template và các identity mới.
Hai lần chạy provisioning bằng process riêng trả manifest giống hệt nhau,
không upload trùng. HEAD cả hai URL trả `200 image/svg+xml`. Lệnh provisioning
không kết nối DB và không chạy lúc backend startup. Backend restart giữ nguyên
URL trong dữ liệu PostgreSQL đã được kiểm thử.

## Migration

`20261024081450_AddSandboxTestIdentities`:

- Thêm `Accounts.IsSandbox`, default false cho dữ liệu cũ.
- Thêm `SandboxIdentityTemplates`, `SandboxTestIdentities`, `SandboxIdentitySessions`.
- Thêm FK/index và unique mapping Developer + Mini App, hash ticket/cookie.
- Down từ chối bỏ dấu Sandbox khi còn tài khoản test, tránh chuyển chúng thành
  tài khoản thường do mất cột phân biệt.

Đã chạy migration thật trên PostgreSQL disposable và test chặn downgrade.
EF báo không còn pending model changes. SQL Up review được xuất tại
`.codex-tmp/sandbox-test-identities.sql`; chưa thực thi trên DB thật.

## Kiểm thử thực tế

| Nhóm yêu cầu | Kết quả | Bằng chứng |
| --- | --- | --- |
| 1. Khởi tạo template/Account/User/profile | Passed | SQLite/PostgreSQL, trường profile, marker, số bản ghi và URL Cloudinary |
| 2. Developer A | Passed | Chrome, login Developer thật qua BFF; code + PKCE, ID token ký thật, UserInfo và trang đã đăng nhập của partner |
| 3. Developer B và cách ly | Passed | Browser context riêng; subject/account partner khác; đọc API app A bị 404 |
| 4. Mở lại và restart | Passed | Sub/account partner giữ nguyên khi mở lại; restart WebApplication/DI/crypto context với PostgreSQL giữ nguyên không tạo trùng Account/sub/URL |
| 5. Bảo mật | Passed | Ticket/code replay, sai PKCE/issuer/audience/redirect, expiry, source revoke, sai owner/client/Origin, scope `openid` không lộ profile, Production JWT scheme từ chối Sandbox token, thời hạn token 30 giây được giữ |
| 6. Cloudinary | Passed | Resource thật, HEAD 200, lần provision sau tái sử dụng version; URL còn nguyên sau restart host |
| 7. Browser E2E | Passed | Chrome headless chạy thực tế, không nhập social password; hai consent đầu tiên vẫn hiện; partner tự tạo/tái sử dụng account bằng issuer/client/sub |

Tổng kiểm tra cuối:

- Build BE/tool host: Passed, 0 warning / 0 error.
- 10 test Sandbox chuyên biệt: Passed.
- Đợt hồi quy SSO + migration liên quan: 189 Passed.
- FE Developer/BFF: 81 Passed.
- Frontend TypeScript và changed-file ESLint: Passed.
- Browser E2E: Passed; viewport 375px không tràn ngang. Ảnh:
  `.codex-tmp/sandbox-identity-mobile.png`.
- Toàn bộ BE ở bản cuối: **910 Passed, 3 Failed, 1 Skipped / 914**.

Ba lỗi còn lại thuộc
`TrustedProxyHeadersTests.HttpDiscoveryReportsPeerEvenWhenProtoIsMissingOrConsumed`
với `(null,false)`, `("https",false)`, `("http",true)`: nội dung/sự hiện diện log
proxy không khớp assertion. `TrustedProxyHeaders.cs` không được thay đổi trong
task này; không sửa cấu hình trust proxy Production để làm xanh các test đó.
Một test MoneyAudit được đánh dấu Skip sẵn bằng `PostgresAuditFact`; không tính Passed.

Các lỗi migration fixture do cột Account mới và test expiry dùng metadata DB đã
được sửa; test expiry dùng đồng hồ OpenIddict thật, không sleep hoặc token mock.
Generic OIDC harness tách policy identity; nhóm Sandbox và browser E2E dùng đầy đủ
registration của ứng dụng. Không dùng harness generic làm bằng chứng cách ly Sandbox.

## Phạm vi chưa chạy

- Render/deployed FE/DB thật: Not Run; chưa có rollout hoặc migration thật trong task.
- Website Mini App bên ngoài thực tế: Not Run. E2E dùng partner backend local có
  OAuth thực, kiểm tra signature/issuer/audience/state/nonce/PKCE và UserInfo thực.
- Giao dịch/ví/thông báo thật: Not Run, không phải hành vi của tài khoản test.

Partner thực tế vẫn cần tích hợp OAuth/SDK, callback và storage Sandbox của họ.
Không map tài khoản bằng email mẫu chung; dùng issuer/client/sub đã xác minh.

## File triển khai

Backend (đường dẫn tương đối từ repo `viora-BE`):

```text
Viora.Domain/Entities/IdentityEntities.cs
Viora.Domain/Developers/SandboxTestIdentityEntities.cs
Viora.Application/Developers/SandboxTestContracts.cs
Viora.Infrastructure/Developers/SandboxTestOptions.cs
Viora.Infrastructure/Developers/SandboxTestService.cs
Viora.Infrastructure/Security/JwtTokenService.cs
Viora.Infrastructure/Sso/SsoConsentService.cs
Viora.Infrastructure/Sso/SsoTokenService.cs
Viora.Infrastructure/Sso/SsoProfileClaims.cs
Viora.Infrastructure/Persistence/AppDbContext.cs
Viora.Infrastructure/Persistence/Configurations/IdentityConfigurations.cs
Viora.Infrastructure/Persistence/Configurations/SandboxTestIdentityConfigurations.cs
Viora.Infrastructure/Persistence/Migrations/20261024081450_AddSandboxTestIdentities.cs
Viora.Infrastructure/Persistence/Migrations/20261024081450_AddSandboxTestIdentities.Designer.cs
Viora.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs
viora-BE/Program.cs
viora-BE/viora-BE.csproj
viora-BE/Developer/MiniAppApi.cs
viora-BE/Developer/SandboxTestApi.cs
viora-BE/Developer/SandboxProfileProvisioningCommand.cs
viora-BE/Developer/SandboxProfile/avatar.svg
viora-BE/Developer/SandboxProfile/cover.svg
viora-BE/Sso/SandboxTestController.cs
viora-BE/Sso/SandboxProtocolObserver.cs
viora-BE/Sso/SsoAuthorizationController.cs
viora-BE/Sso/SsoSessionHandler.cs
viora-BE/Sso/SsoRegistration.cs
viora-BE/Sso/SsoTokenController.cs
```

FE (repo `viora`): `mini-app-sandbox.tsx`, `mini-app-sandbox.css`,
`mini-app-sandbox-account.tsx`, `lib/sandbox-account.ts`, BFF `account-proxy.cjs`,
`sandbox-policy.cjs`; test `sandbox-account.test.ts`/`.test.cjs`; `.env.example`.

Test/tool BE: `SandboxTestIdentityTests.cs`, `SsoProtocolTestHost.cs`,
`MiniAppSandboxSessionTests.cs`; `tools/SsoE2EHost/SandboxIdentityBrowserFixture.cs`,
`tools/SsoE2EHost/Program.cs`; `tools/SandboxIdentityE2E/{fixture.mjs,portal.fixture.jsx,run-fixture.mjs}`.

Spec/decision/runbook: `docs/specs/sandbox-test-identity.md`,
`docs/decisions/sandbox-test-accounts.md`,
`docs/operations/sandbox-test-identity.md`, `.env.example`; FE handoff cùng tên.
Các thay đổi không liên quan có sẵn trong working tree được giữ nguyên.

## Sử dụng và môi trường

Xem [runbook triển khai](../operations/sandbox-test-identity.md) để áp dụng
migration, deploy BE/FE, cấu hình issuer/certificates/BFF hiện có và các biến
`SandboxTest__*`. Localhost Portal cần origin được cho phép rõ ràng.

Sau rollout: đăng nhập Developer → Mini App → Sandbox → cấu hình OAuth client
Sandbox/callback/domain → **Chạy thử Mini App** → consent nếu cần → xem UserInfo,
kết quả và log. Không cần mật khẩu chung, tạo account thủ công hoặc domain Sandbox riêng.
