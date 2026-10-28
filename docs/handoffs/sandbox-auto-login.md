# Sandbox Automatic Login — 26/10/2026

> Update: the host-wide Production disable described in this original report has
> been replaced by explicit opt-in on the existing SSO host. See
> [shared-host handoff](sandbox-shared-host.md) and the current
> [runbook](../operations/sandbox-auto-login.md). Other isolation rules remain.

## Kết quả và phạm vi

Đã sửa trực tiếp BE/Portal, chạy migration PostgreSQL thật và OIDC/Chrome E2E
local. Hai Developer khởi chạy đồng thời không cần mật khẩu hoặc consent, cùng
nhận một User database nhưng subject/phiên/token riêng. Không deploy hoặc migrate
database ứng dụng. Các database kiểm thử đều disposable, được xóa khi test kết thúc;
ID dưới đây là bằng chứng bản ghi thật trong lần chạy, không phải tài khoản đã
được tạo trong database đang cấu hình ở `.env`.

## A. Database

E2E cuối, lúc **08:37:18 ngày 26/10/2026 (Asia/Saigon)**:

| Trường | Giá trị thực tế |
| --- | --- |
| Account ID | `fe34c924-5077-495b-8738-040912273175` |
| User ID | `67e1e500-863e-4e92-a8b2-3572f64cdbb0` |
| Số Account Sandbox | 1 |
| Mapping Developer–Mini App | 2, cùng Account ID |
| Phiên / UserInfo thành công | 3 / 3 |
| AccountType / IsSandbox | `SandboxTest` / true |
| Username | `ankt_sandbox_test` |
| DisplayName | `ANKT Sandbox User` |
| FullName | `ANKT Developer Test` |
| Email | `sandbox-test@domain.test` |
| Gender / IsActive / Role | Male / true / User |
| User.IsVerified | true, chỉ là cờ hồ sơ mẫu |
| Password / EmailVerified / KYC | Không password, email chưa xác minh, không tạo KYC |

AccountType/username/fullName được ánh xạ qua DTO/template bổ sung theo schema
hiện có; Account/User và quan hệ FK là dữ liệu thật. Seed idempotent, tái sử dụng
Account Sandbox hợp lệ có email dành riêng; không nhận tài khoản thật/Admin.

Avatar và cover lưu trên User, HEAD thực tế đều **200 image/svg+xml**:

- `https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829497/ankt/sandbox/avatar-v1.svg`
- `https://res.cloudinary.com/xxgrbmfg/image/upload/v1792829499/ankt/sandbox/cover-v1.svg`

Tái sử dụng asset có sẵn, không upload lại. Bằng chứng database đã lọc bỏ
credentials: `.codex-tmp/sandbox-auto-database.json`.

## B. Audit và Authentication

Đã có sẵn: OpenIddict, code + S256 PKCE, registry/client/secret, callback/domain
validation, owner API/BFF, launch ticket, cookie Sandbox riêng, session revocation,
pairwise subject, SDK và UserInfo database. UserInfo của luồng này không phải mock.
Managed Sandbox/executor là công cụ chẩn đoán riêng; không dùng kết quả mô phỏng
của nó để khẳng định đăng nhập thật.

Sai khác/lỗi đã xử lý:

- Thiết kế trước tạo Account/User theo Developer–Mini App; nay mapping cùng trỏ
  một tài khoản mẫu, vẫn giữ subject riêng theo Mini App.
- Consent lần đầu khiến luồng chưa tự động; nay chỉ cookie của phiên Sandbox đã
  xác thực/bound mới tự cấp quyền. Production và `prompt=consent` giữ chính sách cũ.
- Thiếu chặn môi trường Production; nay Production luôn tắt feature, môi trường
  khác cần bật rõ ràng. Overview bị tắt không tạo dữ liệu.
- Profile thiếu User ID/marker/username; nay scope profile trả dữ liệu database
  qua `id`, `username`, `displayName`, `fullName`, `avatarUrl`, `coverUrl`,
  `isSandbox` cùng claim OIDC. Gender cũng đọc từ User, không hardcode.
- Claim boolean qua OpenIddict có thể đổi cách viết; đọc bằng boolean parser,
  tránh `isSandbox` bị trả false sau khi đổi token.
- Seed gặp tài khoản Sandbox hợp lệ có sẵn từng báo trùng email; nay tái sử dụng,
  chỉ từ chối tài khoản thật hoặc không an toàn.

SSO/code/PKCE/auto login/UserInfo/reopen/restart: **Passed local**. Không thêm hệ
thống auth hoặc endpoint phát access token riêng. Refresh grant vẫn tắt theo chính
sách SSO; không cấp refresh token dùng chung. Test xác minh đăng nhập lại với
`prompt=none` qua phiên Sandbox còn hiệu lực sau khi access token hết hạn.

Production JWT authentication từ chối token Sandbox; Sandbox UserInfo từ chối
Bearer Production. Tài khoản Sandbox không được cấp social JWT/SSO session và bị
ẩn khỏi query Account/User thông thường. Không có Sandbox grant ghi hồ sơ,
thanh toán hoặc gửi notification thật. Mini App đối tác phải tự phân vùng dữ liệu
ghi của mình bằng issuer/client/sub; ANKT không thể bảo đảm storage của bên ngoài.

## C. Kiểm thử thực tế

| Nhóm | Passed | Failed | Skipped | Tổng |
| --- | ---: | ---: | ---: | ---: |
| Sandbox bản cuối: identity + API + migration upgrade + isolation/reuse | 18 | 0 | 0 | 18 |
| Hồi quy SSO trên assembly bản cuối | 176 | 0 | 0 | 176 |
| SDK hiện có | 37 | 0 | 0 | 37 |
| Portal/BFF/CSP | 7 | 0 | 0 | 7 |
| Toàn BE tại checkpoint trước các regression bổ sung cuối | 914 | 3 | 1 | 918 |

Không cộng các dòng thành tổng unique vì bộ toàn BE chứa các test Sandbox.
Nhóm SSO bản cuối cũng bao phủ consent, session, token, registry và certificate;
chạy sau các sửa đổi seed/profile cuối, dùng assembly output riêng.

Backend tests dùng SQLite và PostgreSQL local thật, không kết nối DB ứng dụng.
Migration upgrade kiểm tra rebind, revoke phiên cũ, giữ tài khoản Production và
không còn pending model changes. Bao phủ seed hai lần, tài khoản có sẵn, sai owner,
anonymous/nhầm scheme, sai Origin/client/redirect/PKCE, ticket/code replay, hết hạn,
thu hồi từng phiên không ảnh hưởng token phiên khác, đọc profile DB hiện tại,
openid-only không lộ profile, từ chối token Sandbox ở Production API.

Chrome E2E bản cuối: **Passed**; hai browser context đăng nhập Developer thật qua
BFF, mở hai Mini App local đồng thời, code/PKCE, ID token ký thật và UserInfo thật,
zero consent prompts, mở lại cùng subject, khác owner bị 404, mobile 375px không
tràn ngang. Partner E2E là confidential backend dùng API OIDC thật; không khẳng
định đã chạy SDK browser trên website đối tác ngoài thực tế.

Build BE/E2E host: **0 warning, 0 error**. Frontend TypeScript và ESLint file đổi:
Passed. xUnit có một warning analyzer tồn tại sẵn trong TrustedProxyHeadersTests.

Ba lỗi toàn BE đều thuộc
`TrustedProxyHeadersTests.HttpDiscoveryReportsPeerEvenWhenProtoIsMissingOrConsumed`
với `(null,false)`, `("https",false)`, `("http",true)`: assertion sự hiện diện/nội
dung log proxy không khớp. Đã có trong handoff 24/10; file proxy và test này không
được sửa trong task. Không thay policy trust proxy Production để làm xanh test.
MoneyAuditPostgresTests bị skip vì không cấp `VIORA_MONEY_AUDIT_TEST_DB`; không
đánh dấu Passed hoặc tự trỏ sang database ứng dụng.

Artifacts: `.codex-tmp/test-results/sandbox-auto/*.trx`, log
`.codex-tmp/sandbox-auto-{final,full,sso-final,browser-final,sdk,portal}.log`,
ảnh `.codex-tmp/sandbox-identity-mobile.png`. Các lần RED là bằng chứng regression,
không cộng vào kết quả bản cuối. Lỗi pg_ctl/esbuild trong sandbox ban đầu được
giải quyết bằng chạy test local với quyền tạo child process; không đổi database đích.

## D. Source code

Migration mới: `20261026012608_ShareSandboxTestAccount` (+ Designer và snapshot).
Bỏ unique AccountId trên mapping, giữ unique Developer/MiniApp, revoke phiên cũ,
rebind sang template; giữ account cũ cho audit, không ghi tài khoản thật. Downgrade
từ chối khi còn nhiều mapping chung. Migration chỉ đã áp dụng trên DB test.

File sửa trong BE (đường dẫn tương đối repo):

- `Viora.Domain/Developers/SandboxTestIdentityEntities.cs`
- `Viora.Application/Developers/SandboxTestContracts.cs`
- `Viora.Infrastructure/Developers/SandboxTestOptions.cs`, `SandboxTestService.cs`
- `Viora.Infrastructure/Persistence/Configurations/SandboxTestIdentityConfigurations.cs`
- `Viora.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- `Viora.Infrastructure/Sso/SsoConsentService.cs`, `SsoTokenService.cs`, `SsoProfileClaims.cs`
- `viora-BE/Sso/SsoAuthorizationController.cs`, `SsoTokenController.cs`
- `Viora.Application.Tests/SandboxTestIdentityTests.cs`, `SsoProtocolTestHost.cs`
- `tools/SsoE2EHost/SandboxIdentityBrowserFixture.cs`
- `tools/SandboxIdentityE2E/fixture.mjs`, `run-fixture.mjs`, `.env.example`

Test mới: `SandboxAutoLoginMigrationTests.cs`, `SandboxAutoLoginEndpointTests.cs`,
`SandboxAutoLoginProtocolTests.cs` trong `Viora.Application.Tests/`.

File sửa trong FE repo `viora`:
`features/developer/lib/sandbox-account.ts`,
`features/developer/components/mini-app-sandbox-account.tsx`,
`packages/miniapp-sdk/README.md`. SDK runtime giữ API hiện có; không thêm API trùng.

Không thêm route mới; tái sử dụng `/sandbox-test`, `/sandbox-test/sessions`,
`/sso/sandbox-test/start` và `/connect/*` đã có. Các thay đổi working tree có sẵn
được giữ nguyên; chưa stage/commit/push.

Tài liệu mới: spec `sandbox-auto-login.md`, quyết định
`sandbox-shared-auto-login.md`, runbook và handoff này. Tài liệu cũ có đánh dấu
superseded để tránh triển khai theo thiết kế tài khoản riêng trước đây.

## E. Hướng dẫn Developer và phần còn lại

Đăng nhập Portal test → chọn Mini App → Sandbox → cấu hình Sandbox client/callback/
domain → **Chạy thử Mini App** → Mini App tự hoàn tất OAuth → gọi
`sdk.auth.getUser()` → đối chiếu `id`, `username`, `isSandbox` và ảnh.
SDK thực tế dùng `initialize/login/handleRedirect/getStatus/getUser`.
Token hết hạn: authorize lại qua phiên còn hiệu lực; hết phiên: mở lại từ Portal.

Review cuối: giữ PKCE/state/nonce/redirect/client validation, scope tối thiểu,
cookie HttpOnly/Secure/Lax, ticket hash/dùng một lần, source-session/owner/client
binding và revocation. Không thêm dependency, credentials vào URL hoặc token vào
localStorage. Không phát hiện lỗi bảo mật nghiêm trọng còn mở trong phần thay đổi;
ba lỗi toàn BE phía trên vẫn được báo Failed.

Xem [runbook](../operations/sandbox-auto-login.md) cho cấu hình, code SDK và xử lý
lỗi. **Chưa hoàn tất rollout môi trường ứng dụng:** chưa kiểm tra/tạo Account/User
trong database `.env`, chưa migrate/deploy backend/Portal đang vận hành, chưa E2E
với website Mini App bên ngoài. Cần môi trường test được xác định và quyền triển
khai tương ứng trước các bước đó. Không thay môi trường của dịch vụ Production
để bật auto login. Không tuyên bố tiêu chí hoàn thành trên môi trường triển khai
đã đạt chỉ từ kết quả local.
