# ANKT Developer Platform — Stage 8 specification

Status: **APPROVED FOR IMPLEMENTATION**, 2026-10-22, bằng yêu cầu người dùng
“chạy làm giai đoạn 8”. Thực hiện theo dependency và ghi bằng chứng tại
[handoff](../handoffs/developer-platform-stage-8.md). Ngày 2026-10-23 người dùng
chốt Web trước; native chưa nghiệm thu. Xem [Web completion checkpoint](../handoffs/developer-platform-stage-8-acceptance.md)
cho bằng chứng và giới hạn; chưa nghiệm thu toàn bộ Stage 8 cross-platform.
Nguồn: yêu cầu “Mini App Discovery, Production Host Runtime & ANKT Integration”,
mục I–XXVI. [Implementation plan](../plans/developer-platform-stage-8.md).
Phê duyệt Stage 7 không thay thế gate phê duyệt Stage 8 tại mục XXIV.

## 1. Objective và phạm vi

Thêm section “Mini App” ngay dưới Ví ở tab Tiện ích. Người dùng xem catalog thật,
mở đúng publication Production, sử dụng website/SDK được hỗ trợ, đăng nhập qua
OIDC khi cần và đóng về Tiện ích. Hỗ trợ Web, Android và iOS bằng adapter rõ ràng;
trạng thái implementation, build và E2E của mỗi nền tảng được báo cáo riêng.

Giữ header, WalletSummaryCard, thanh toán/payOS, Social Auth, refresh, theme,
navigation và các chức năng hiện có. Không category filter, app giả, auto-publish,
deploy, production migration, cấp native permission hoặc bắt đầu Stage 9.

## 2. Audit và dependency gate

Audit source hiện tại và các spec/plan Stage 5/6/7 thực sự tồn tại dưới `docs/`.
Handoff cuối supersede các checkpoint lịch sử còn ghi chưa hoàn thành.

| # | Dependency | Source đã đối chiếu / kết luận |
| --- | --- | --- |
| 1 | Expo structure | `../viora/app`, `features`, `components`, `services`, `stores`, `theme`; Expo Router, không phải ứng dụng Next.js. |
| 2 | Tiện ích | `features/utilities/utilities-screen.tsx`, `app/(tabs)/utilities.tsx`; hiện Wallet rồi future banner, chưa có catalog. |
| 3 | Wallet | `components/wallet/wallet-summary-card.tsx`, `services/wallet.service.ts`; loading/error/retry riêng. Giữ nguyên component và luồng nạp/rút/lịch sử. |
| 4 | Navigation | `app/_layout.tsx`, `app/(tabs)/_layout.tsx`; root guard buộc login ngoài `sso`/`developers`. Cần ngoại lệ route host công khai có giới hạn; không mở anonymous toàn bộ tabs. |
| 5 | Auth/session | `services/authenticated-fetch.ts`, `stores/session-store.ts`; reuse ensureFreshSession/refresh coordinator, không tạo token store khác. |
| 6 | API client | Wallet service dùng authenticatedFetch và EXPO_PUBLIC_API_URL; catalog cần anonymous fetch riêng, session chỉ gửi tới first-party API. |
| 7 | Registry | `Viora.Domain/Developers/MiniAppEntities.cs`, `MiniAppService`; metadata mutable không phải nguồn public Production. |
| 8 | Publishing | `MiniAppPublishingService`, `MiniAppPublicationEntities`; publication gắn version/review; approve không publish; hỗ trợ non-SSO với nullable ProductionOAuthClientId. |
| 9 | Versioning | `MiniAppVersionSnapshot`, `MiniAppVersionSnapshotPolicy`; schema 1/canonical hash/immutable target. Chưa có reviewed native capability manifest. |
| 10 | Suspension | `MiniAppModerationService`, operational states, `MiniAppPublicationRevocation`; suspend đóng publication/thu hồi grants, restore không republish. |
| 11 | Domain | `MiniAppDomainLifecycle`, `MiniAppReadinessService.TargetAsync`; proof theo hostname, expiry/revoke, public HTTPS fetch policy. Dùng domain của snapshot đích. |
| 12 | Production client | `MiniAppPublicationPolicy.ReadAsync` hiện cần OAuth client và SSO=true; không dùng trực tiếp làm catalog gate cho non-SSO. Cần eligibility chung không làm yếu SSO gate. |
| 13 | OIDC server | `SsoClientReader`, `SsoClientPolicy`, `SsoRegistration`, authorization/token/UserInfo; OpenIddict 7.7.2 và registry projection hiện có. |
| 14 | Discovery | Stage 5 discovery/JWKS, HTTPS issuer, PKCE/state/nonce/signature và environment isolation đã có test; không tạo issuer/token format khác. |
| 15 | First-party SSO | `features/sso/bootstrap.ts`, `app/sso/login.tsx`, `SsoBootstrapService`, `SsoSessionService`; browser-bound transaction/live sid. Trang login chủ động từ chối native. |
| 16 | SDK | `packages/miniapp-sdk/src/auth.ts`, `popup.ts`, `bridge.ts`; Public memory tokens và Confidential backend-owned session. Không copy token vào ANKT host. |
| 17 | Bridge | v1 chỉ getContext/close; v1 context đã chấp nhận Production/controlled-web. v2 methods hiện buộc Sandbox/sandbox-web. Không đổi nhãn Sandbox thành Production bằng frontend. |
| 18 | Sandbox host | `MiniAppSandboxHostApi`, runtime/grant/session services, `SandboxHost/{host,bridge,external-link}.mjs`; reuse pattern origin/source/nonce/CSP, không dùng lại DeveloperSession/cookie/executor authority cho user Production. |
| 19 | Admin | `Admin/MiniAppReviewAdminApi`, moderation API và `../viora-admin/src/components/MiniAppReview`; giữ nghiệp vụ review/moderation hiện có. |
| 20 | DB | AppDbContext/EF PostgreSQL, version/review/publication/session tables, migration triggers và IsolatedPostgres tests. Chưa có user Production host launch store. |
| 21 | Native | Expo ~54.0.34, RN 0.81.5, React 19.1.0, Router ~6.0.23, WebBrowser ~15.0.11. Chưa khai báo react-native-webview; bundledNativeModules của Expo cài local chọn 13.15.0. Android source + adb/java có; chưa chạy device/build audit. Không có ios directory/Xcode trên workstation Windows. |
| 22 | Device permissions | `app.json` đã khai báo camera/microphone/location cho tính năng khác. Không được coi quyền ANKT đã có là quyền Mini App; native deny phải kiểm thử cả khi các quyền này đã granted. |
| 23 | Tests | xUnit/TestHost/IsolatedPostgres, Node/tsx SDK/BFF, real HTTPS/CDP executor/review fixtures có sẵn. Chưa có Stage 8 runtime/native E2E. |

Dependencies Stage 5/6/7 **hoàn tất trong phạm vi local đã duyệt**:
[Stage 5 handoff](../handoffs/developer-platform-stage-5.md),
[Stage 6 acceptance](../handoffs/developer-platform-stage-6-acceptance.md),
[Stage 7 acceptance](../handoffs/developer-platform-stage-7-acceptance.md).
Baseline đã xác minh: backend 695 pass/1 MoneyAudit skip có sẵn; Developer/BFF 63,
Admin 18, SDK 37, host/executor 23; Release/model/types/lint/export và Stage 7
Public/Confidential A–D pass. Đây là evidence trước Stage 8, **không phải test mới**.
Rollout DB, origins, DNS/TLS/headers/SMTP ngoài fixture vẫn chưa được thực hiện.
Native runtime, native bootstrap và native partner-session handoff là dependency
mới phải chứng minh; không gán chúng thành phần “đã có” của Stage 5/6/7.

## 3. Assumptions đề xuất để human duyệt

1. Catalog/detail và non-SSO launch được anonymous. SSO-enabled app vẫn có thể mở
   phần public; login chỉ khi người dùng/partner yêu cầu. Schema 1 chưa có policy
   “bắt login ANKT khi launch”; không tự suy diễn SsoEnabled thành policy này.
2. Route `/mini-apps/[appId]` độc lập protected tabs, chỉ mở host có validation.
   Thêm ngoại lệ đúng route này ở root guard; mọi auth/social behavior khác giữ nguyên.
3. Dùng HTTPS host origin chuyên biệt, khác app/API/issuer/partner và Sandbox host;
   cùng site với first-party Web nếu triển khai cho phép. Origins là cấu hình cần
   operator chốt, không giả định domain đã được cấp DNS/chứng chỉ.
4. Web và native đều tải trusted host shell; website partner ở iframe cross-origin
   bên trong. Native WebView là adapter container, không đưa ReactNativeWebView API
   trực tiếp vào website partner. Partner phải cho phép các ancestor thực tế.
5. Giữ Bridge v1 getContext/close đúng wire contract. SDK helpers environment/inside
   có thể suy ra từ v1 context/handshake. Không quảng cáo v2 Sandbox là Production.
   auth.*, device permissions, diagnostics v2 và SDK openExternalUrl chưa có contract
   Production: UNSUPPORTED_METHOD. Mở ngoài qua host chrome có xác nhận. Nếu cần thêm
   wire methods, phải có capability negotiation tương thích cũ và review contract.
6. Không thêm capability editor hay giả rằng version cũ đã duyệt quyền native.
   Baseline chỉ context/close; optional permissions mặc định rỗng. Mở rộng cần
   declaration gắn immutable version + Admin approval, không sửa snapshot đã khóa.
7. Thêm projection catalog immutable và launch/grant/audit storage additive.
   Projection chỉ giúp query public metadata/pagination; publication/operational/
   developer/domain/client sống vẫn là authority, không tạo status source thứ hai.
8. Native SSO là checkpoint rủi ro trước feature native: browser hệ thống,
   first-party bootstrap có live sid/browser binding và partner-owned continuation.
   Không giả định cookie browser = cookie WebView. Chỉ triển khai handoff đã chứng
   minh và review; chưa chứng minh thì native SSO giữ disabled, không báo Stage 8
   native hoàn tất. Không hạ Public client thành Confidential hoặc bắt Public app
   thêm backend ngầm để đạt test.
9. Chỉ bổ sung WebView dependency sau duyệt: đề xuất pin 13.15.0 theo Expo local,
   kiểm tra advisory/compatibility trước cài. Nếu không đạt, trình lựa chọn pin mới;
   không nâng Expo/RN ngầm. Native security adapter tối thiểu được phép khi props
   thư viện không đủ, không đổi quyền global của Social/calls/camera.
10. TTL đề xuất là security policy, không phải kết quả đo hiệu năng: host 15 phút
    absolute; grant 30 giây; heartbeat 15 giây; SSO handoff tối đa 2 phút. Server
    cấu hình có giới hạn; close/background/expiry chặn bridge ngay ở client.

## 4. Architecture và data authority

Luồng: Tiện ích -> public catalog -> public detail -> launch API -> one-use grant
-> trusted Production host -> partner iframe -> optional standard Stage 5 OIDC.
User/social auth, Developer auth, SSO authorization, partner session và host launch
session là các context khác nhau. Host session không cấp OAuth consent hay token.

Tạo common Production publication eligibility theo AppId, hỗ trợ SSO và non-SSO:
exact current Published publication, same-app submitted immutable version + Approved
review, canonical snapshot/hash hợp lệ, Active operational state, chưa xóa, owner
Developer còn Active/unlocked, domain proof đúng target còn hạn/chưa revoke. Với
SSO: exact Production client identity, live registry projection/scopes/redirects,
Confidential credential còn hiệu lực và Sso enabled. Không join mutable draft để
lấy tên/logo/website/callback/type. Không lấy MiniApp.Status làm authority duy nhất.

Refactor shared policy có test bảo toàn các entry point SSO cũ; non-SSO không buộc
có OAuth client. Không gọi readiness của Developer/current Sandbox run để launch
user; submission attestation đã sealed không phải quan sát trình duyệt mới.

Catalog projection: 1 row/publication, same-app FK tới exact version/review,
snapshot hash + public display fields, immutable sau insert. Populate trong
transaction publish; query join publication/status/proof/developer/client sống
**trước Count/Order/Skip/Take**. Không page trước rồi lọc in-memory. Không HTTP probe
mỗi item; kiểm tra network target ở launch/runtime. Reconcile existing published
rows bằng explicit dry-run/validation/backfill có transaction; không tạo Approved,
Published hay tự onboard legacy Published. Thiếu projection -> không hiển thị.

Launch thực hiện network preflight ngoài transaction, sau đó recheck đúng publication,
hash, revisions, actor/binding/client/proof trước commit và trước redemption. CAS/
serializable + unique request receipt; payload-bound requestId. Suspend/unpublish/
republish đồng thời không cho redeem session cũ hoặc chuyển sang URL/version khác.

## 5. Proposed storage

| Record mới | Nội dung và invariant |
| --- | --- |
| MiniAppCatalogPublication | Public metadata từ snapshot, publication/version/review same-app FKs, immutable rows, stable publication-time/id sort. Không secret/contact riêng. |
| MiniAppProductionHostSession | App/publication/version/hash/domain/client/policy revisions; platform; nullable AccountId + signed source sid; browser-binding hash; created/expires/closed, concurrency. Anonymous không được nâng quyền bằng client fields. |
| MiniAppProductionLaunchGrant | Hash entropy credential, session/binding/host audience/nonce, expires/consumed, payload-bound replay receipt. Credential chỉ trong HTTPS POST/exact-origin memory message. |
| MiniAppProductionRuntimeEvent | Catalog event code + app/environment/time/correlation; optional internal session reference với retention rõ. Không URL query, token, cookie, user content hay private claims. |

Native SSO persistence chưa được chốt: Task 1–3 phải trình contract/ADR nếu cần
thêm storage hoặc relay authorization response. Không giấu credential broker trong
“telemetry” hay grant store. Migration reviewed SQL + disposable PG Up/Down/Up,
race/rollback/immutability/FKs và offline EF comparison; không DB ứng dụng.
Rollback: disable Stage 8 endpoints/UI, revoke host sessions/grants, giữ Stage 7
publication/SSO guards. Không tắt Stage 7 flag làm rollback và không resurrect tokens.

## 6. Data contracts và API

Response camelCase; lỗi `{error:{code,message,correlationId?}}`, không stack/secret.
App IDs theo registry `app_` + 43 base64url chars. Reject unknown write fields,
duplicate JSON keys, malformed IDs/enums, excessive body/depth; server chọn URL.

| Route đề xuất | Contract/auth/behavior |
| --- | --- |
| GET /api/mini-apps?page=1&pageSize=20 | Anonymous; page 1..100000/pageSize 1..100, stable order. `{items:[{id,name,logoUrl,description,category,version}],page,pageSize,total}`. description lấy ShortDescription rồi bounded fallback; version = VersionName. |
| GET /api/mini-apps/{appId} | Anonymous; public metadata + version + `{canOpen,capabilities,loginAvailable}`; không WebsiteUrl/callback/secret/Developer PII. Unavailable trả 404 APP_NOT_FOUND, không lộ draft/suspension reason. |
| POST /api/mini-apps/{appId}/launch | `{requestId,platform,bindingId,browserNonce}`; không URL/environment/actor/status/capability override. Anonymous allowed; supplied invalid bearer không âm thầm thành anonymous. Web Origin/CSRF and binding checks; native separate scoped policy. |
| GET /api/mini-app-host/prepare | Exact configured host + HTTPS; bounded anonymous shell/binding, không app metadata. Native bootstrap channel phải chứng minh main-frame ownership trước khi nhận grant. |
| POST /api/mini-app-host/redeem | Exact host, one-use grant + binding/nonce/audience; no-store. Establish scoped HttpOnly host cookie, load server-selected partner origin. |
| GET /api/mini-app-host/context | Runtime cookie, live session/publication/domain/client recheck; v1 safe context. Không social/SSO/partner tokens. |
| POST /api/mini-app-host/heartbeat, /close | Same runtime binding/Origin/CSRF; close idempotent, no ANKT logout. Expired/suspended session denied; timers bounded. |
| POST /api/mini-app-host/events | Optional fixed event code/correlation only, bounded quota. Event data không bao giờ cấp authority. |

Launch receipt chỉ gồm `{sessionId,publicationId,version,hostOrigin,hostUrl,
expiresAt,grant,capabilities,correlationId}`; URLs thuộc host first-party, không
credential trong URL/query/fragment. Grant là credential ngắn hạn chỉ cho host
redemption, không token ANKT, không OAuth token, không cho partner. Host mới nhận
website target sau redemption. Response luôn no-store/no-referrer.

Session IDs non-authenticating; biết ID không được đọc/đóng phiên khác. Authenticated
launch validate live Account/status + signed sid/source, không chỉ decode JWT. Policy
cho anonymous và authenticated không chia sẻ replay receipt/binding giữa người dùng.
Rate limits cấu hình scope module theo IP + binding/session và user khi có; proposed
launch 20/phút/binding, prepare 30/phút/IP, 3 active host/binding. Không sửa limiter
của Wallet/Social/Developer. Feature flag `MiniAppRuntime:Enabled=false`, phụ thuộc
Stage 7 review/publishing gate; SSO subfeature cần Sso:Enabled riêng.

2026-10-23 implementation bounds: `MiniAppRuntime` exposes
`PrepareLimitPerMinute` (default/ceiling 30), `LaunchLimitPerMinute` (20, applied
to IP and binding/account creation history), `RedeemLimitPerMinute` (30),
`RuntimeLimitPerMinute` (120), and `EventsPerSessionPerMinute` (10). Each accepts
1 through its ceiling. `ReadLimitPerMinute` retains 120 default and 1–1000 bounds.
Module policies remain independent; lowering one does not alter unrelated routes.

## 7. Tiện ích và host UX

Chèn MiniAppSection ngay dưới Wallet, trước future banner; independent fetch/state/
retry để catalog lỗi không block Ví. Grid tính theo container onLayout và minimum
cell width, khoảng 4 cột khi đủ chỗ; 320px có thể ít hơn; desktop logo không phóng
theo cell. Logo khoảng 52–64px, radius16 theo theme, tên center 2 dòng, safe fallback.
Không dùng UtilityGrid cũ có category/mock và width25% cứng làm Production catalog.

Loading skeleton; empty “Chưa có Mini App khả dụng”; error thân thiện + retry;
pagination/load more bounded và hủy stale response. Giữ page memory cache tối đa
30 giây, refresh focus/reconnect; HTTP no-store để tránh CDN stale authority. Cache
chỉ hiển thị, launch luôn revalidate. Khi app bị thu hồi, invalidate cached item.

MiniAppHostScreen header close/back/logo/name/menu refresh/external; safe area,
progress/loading, error/offline/retry, accessible labels/live status/focus restore.
Không mount WebView trước launch. Header không nhận tên/URL từ untrusted child.
Close unmount ngay, dispose listeners/timers/iframe/WebView, cancel SSO attempt;
server close best effort bounded, absolute expiry là fallback, không logout ANKT.
Android back: đóng modal trước; trusted same-origin history khi có thể xác minh;
không inspect cross-origin iframe history, fallback close. iOS gesture cùng semantics.
Background pause bridge và render-sensitive actions; foreground live recheck trước
resume. Process death không restore bearer grant/PKCE/verifier từ persisted state;
mở lại tạo launch mới, partner session được chính partner xác nhận lại.

## 8. Web host, navigation và native container

Host origin tuyệt đối tách partner và privileged ANKT origins. CSP frame-src exact
partner origin; frame-ancestors exact approved app ancestor(s); permissions-policy
deny camera/mic/location/payment/USB. Partner iframe chỉ scripts/same-origin/forms
và popup support cần cho OIDC; không allow-top-navigation/downloads. Mọi ancestor
phải được partner frame-ancestors cho phép. Không proxy partner vào ANKT origin.

Không suy frame-blocked từ load/onerror đơn lẻ: header observation + SDK handshake
có timeout; không SDK vẫn có thể xem website nhưng không có bridge authority.
Chỉ gán FRAME_BLOCKED khi có bằng chứng; còn lại BRIDGE_UNAVAILABLE/LAUNCH_FAILED
với hướng dẫn kết nối. External fallback explicit user gesture, HTTPS và noopener/
noreferrer; không có ANKT bridge, không tính là embedded-host E2E thành công.

| Navigation class | Chính sách |
| --- | --- |
| Host shell | Exact configured host origin + route allowlist; reject initial URL trước native mount. |
| Partner documents | Exact frozen HTTPS origin; bridge requires current child source + handshake. Reset on observable reload/navigation; CSP giới hạn đổi origin. Không tuyên bố quan sát mọi cross-origin path/network request. |
| OAuth | Exact discovery issuer authorize endpoint và exact approved callbacks; state/nonce/PKCE; không cho general navigation vào first-party account APIs. |
| External | Parsed HTTPS public target, không credentials; explicit confirmation. No automatic Linking.openURL cho wildcard whitelist failure. |
| Deep link | Chỉ first-party completion route được cấu hình và transaction-bound; không forward arbitrary partner custom scheme/intent. |
| Dangerous | javascript/data/file/content/intent/http/unknown scheme blocked; reject deceptive hostname suffix/port/userinfo. Không startsWith(domain). |

Native dùng React Native WebView/WKWebView **chỉ chứa trusted host shell**. Same
iframe v1 postMessage protocol hoạt động bên trong, không tạo SDK fork hay trust
data.origin tự khai báo từ RN onMessage. Nếu cần private shell→native close/back/
SSO channel: verify actual top document, one-use session nonce/generation và fixed
schema/rate; nonce không lộ cho child; native onMessage alone không chứng minh origin.
Chứng minh subframe không spoof được channel; nếu không, disable channel/adapter.

Android: validate source trước first load (onShouldStartLoadWithRequest không được
gọi lần đầu), restrict navigation/new windows, TLS fail closed, mixedContentMode never,
file/universal file access false, geolocation false, remote debugging release off.
Không setSupportMultipleWindows=false như giải pháp chặn popup; xử lý new window
bằng policy. Native permission/file-chooser denial phải là security boundary thực,
không dựa vào injected JS hay chỉ permission chưa được cấp trên test device.
iOS: mediaCapturePermissionGrantType=deny khi API hỗ trợ, WK delegates/chooser
policy tương ứng, no arbitrary cookie sharing với ANKT browser. Giữ keyboard/safe
area, handle WK process termination. Không tự sửa global app permission/cookie store.
Upload/download/device APIs mặc định unsupported trong Stage 8 baseline.

## 9. Production SSO và compatibility checkpoint

Web reuse Stage 5 SDK Public popup PKCE và Confidential backend exchange; actual
first-party bootstrap, consent mới/scopes mới/deny, repeat consent fast path. Không
silent iframe dependency; Public tokens ở partner memory, Confidential secret/token
ở backend partner, opaque HttpOnly partner cookie. Host không trở thành OAuth client.

Native phải sử dụng system browser/auth-session cho authorization, giữ registered
HTTPS partner callback. Không copy ANKT JWT, issuer cookie, PKCE verifier hoặc
partner cookie vào WebView/URL. Nếu browser không có ANKT session, cần first-party
browser-bound bootstrap từ live native session; không sửa social login/refresh.

Task 1–3 phải chứng minh bằng real isolated issuer/partner trên native:
- Browser có session khác user native, login CSRF, cancellation, app resume/death.
- Public: SDK iframe giữ state/nonce/verifier; callback trở về đúng initiating
  SDK transaction và library exchange/validation thật, không bật embedded-password
  authorization hoặc đổi loại client để né handoff.
- Confidential: callback đổi token ở backend, partner-owned single-use continuation
  nếu cần để tạo/revalidate WebView session; không giả browser cookie đã có ở WebView.
- Receipt/deep link chỉ báo có kết quả, không tự authenticated; partner phải verify
  transaction/one-use/browser binding và fresh UserInfo/session. Social JWT không
  đi qua partner, bridge hay navigation URL.

**Gate P0:** transport, endpoint/auth/CSRF, nonce/state binding, callback provenance,
expiry/replay, cookie behavior và redacted storage phải có contract/ADR + tests.
Hiện chưa có native handoff tương thích được xác minh trong repo. Nếu cần broker
authorization-code/credential mới, thay standard callback, thêm partner backend cho
Public hoặc thay trust boundary, trình human review trước implementation đó.
Không ghi native SSO success từ bridge notification/session flag/mock.

## 10. Permissions, errors, analytics và performance

Declared/approved/user-consented/OS-granted là bốn tầng riêng. Schema 1 không có
reviewed native permissions, nên không được expose device/auth bridge capabilities.
OAuth scopes chỉ điều khiển OIDC claims. Unsupported methods trả UNSUPPORTED_METHOD;
forged/expired handshakes không làm isInsideAnkt=true. No social access token relay.

Errors: APP_NOT_FOUND, APP_NOT_PUBLISHED, APP_SUSPENDED, DEVELOPER_SUSPENDED,
DOMAIN_INVALID, LAUNCH_FAILED, WEBVIEW_LOAD_FAILED, FRAME_BLOCKED, SSO_REQUIRED,
SSO_FAILED, CONSENT_DENIED, BRIDGE_UNAVAILABLE, BRIDGE_TIMEOUT, UNSUPPORTED_METHOD,
NAVIGATION_BLOCKED, NETWORK_UNAVAILABLE. Public detail/list không disclose private
status reasons; scoped active host có thể nhận safe revoked error. 400 malformed,
401 session required/invalid, 404 inaccessible app/session, 409 stale publication,
429 bounded rate, 503 unavailable dependency; no private provider response passthrough.

Events chỉ opened/launch_failed/closed/runtime_error/sso_started/sso_failed/
bridge_error với server-safe catalog, correlation/time/environment/appId. Không raw
errors/console/network payloads, email, tokens, codes, cookies, URLs có query.
Đề xuất retention 7 ngày cho runtime events, expired grant purge sau 24h; session
cleanup giữ tối thiểu phục vụ replay/audit theo runbook. Không external analytics.

Measure catalog query count/latency, rows/page, launch timing, logo requests, JS/
native memory và mount/unmount cycles trên fixture/device. Không tự đặt benchmark
rồi báo đạt. Không full-registry HTTP probes/N+1, không block Wallet, không preload
WebView, tránh SSO retry loop và unlimited retained pages/listeners.

## 11. Tests, commands và success criteria

Backend matrix đủ 16 mục XXII; frontend matrix đủ 20 mục XXI, traceability theo
test names trong final security/handoff. RED/GREEN từng slice, concurrency +
rollback/migration trên disposable PostgreSQL, không thay assertions bằng seeded success.
Real E2E A non-SSO: real Stage 6 submission/Admin approval/owner publish -> Utilities
-> catalog -> host interaction -> close -> Utilities. Non-SSO có thể thiếu SDK nhưng
website vẫn mở; bridge authority chỉ khi handshake hợp lệ. Nếu Stage 7 submission
đòi bridge proof, fixture non-SSO vẫn tích hợp SDK và không giả attestation.
E2E B Public và Confidential: login ANKT, first/deny/retry/repeat consent, actual
code/token/ID token/UserInfo/partner session, reload/reopen; new scopes need consent.
C: active host + new launch/catalog sau suspend; tokens revoked; restore remains
unpublished, explicit republish. D: exact redirect, cross-origin/source/forged bridge,
unpublished/version swap, Sandbox resource isolation, dangerous scheme, secret
redaction, invalid TLS and denied device permissions. Test actual two clients/user
sessions, lost replies, duplicate grant redemption, publication/suspend races.

Commands từ `viora-BE` (SDK 8 local đã có; không dùng application config cho fixture):

```powershell
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~MiniAppProduction'
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=stage8-final.trx'
& "$env:DOTNET_ROOT/dotnet.exe" build viora-BE.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
& "$env:DOTNET_ROOT/dotnet.exe" ef migrations has-pending-model-changes --project Viora.Infrastructure --startup-project viora-BE --no-build
```

Commands từ `../viora`:

```powershell
npm run test:developer
node --import tsx --test --experimental-test-isolation=none features/sso/bootstrap.test.ts features/mini-apps/tests/*.test.ts
node --test components/wallet/wallet-summary-card.test.mjs
npx tsc --noEmit
npx eslint features/mini-apps components/mini-apps services/mini-app.service.ts 'app/mini-apps' features/utilities/utilities-screen.tsx app/_layout.tsx
npm run build:web -- --output-dir .codex-tmp/stage8-web-export
npm --prefix packages/miniapp-sdk test
```

Fixture Web đã có: `node --test tools/MiniAppProductionE2E/production-partner.test.mjs`;
`node tools/MiniAppProductionE2E/run-fixture.mjs --runtime-security`, biến thể
`--confidential-partner --runtime-security`, và riêng `--non-sso-partner`.
Không có cờ `--all-flows` hay native runner đã nghiệm thu. Tất cả owned
loopback services/profiles/DBs, bounded deadline, nonzero on failure, cleanup verified.
Native sau approval/dependency: `adb devices -l`, Android `gradlew.bat :app:assembleDebug`
với public isolated fixture config, `adb -s <device-id> install -r <fixture-apk>`;
iOS cần Mac/Xcode + simulator/device, `npx expo run:ios --device <device-id>` và
fixture runner tương ứng. Không install lên device không xác định hoặc dùng release
signing keys. Generated native files phân biệt với authored adapter/plugin files.

Complete khi từng platform có build + runtime evidence phù hợp, all required
backend/frontend/security cases và A–D đạt, không open high-severity finding,
Wallet/Social/Stage 5–7 regressions giữ nguyên; handoff đủ 21 nhóm mục XXVI.
Nếu thiếu iOS device/simulator: adapter/types/tests có thể hoàn tất nhưng ghi
**iOS E2E NOT VERIFIED**, không tuyên bố full cross-platform acceptance. Nếu native
SSO handoff không đạt P0: báo blocked requirement, không gọi external fallback là đạt.

## 12. Boundaries, rollout và open questions

Always: preserve unrelated dirty edits, <=5 authored files/task hoặc split trước;
tests/build/review theo affected scope, records kỹ thuật đúng evidence. Không auto
commit, package publication, SDK/framework upgrades, production fixture traffic,
real email, TLS bypass hay seed approvals/publications làm bằng chứng nghiệp vụ.
Approval spec chỉ cho additive schema/contracts/tasks đã chốt, không deploy hay
schema change ở DB ứng dụng. Thay identity/trust boundary/capability cần review.

Open questions để review (defaults đề xuất ở mục 3):
1. Duyệt anonymous catalog/public host + ngoại lệ root route giới hạn?
2. Duyệt dedicated host-shell origin, iframe trên cả Web/native và explicit external
   fallback cho partner không cho nhúng? Chốt actual origins ở rollout, không đoán DNS.
3. Duyệt Bridge v1 baseline context/close, helpers environment/inside, device/auth/
   openExternalUrl RPC unsupported; hay cần thêm reviewed capability schema ngay?
4. Duyệt pin WebView 13.15.0 subject to advisory/compatibility gate và native deny adapter?
5. Duyệt native SSO feasibility gate P0; handoff chi tiết phải review nếu thêm broker
   hay thay trust model. Có môi trường Android/iOS nào được phép dùng khi triển khai?
6. Duyệt projection/backfill dry-run, host TTL/quota/event retention đề xuất?

References chính thức, kiểm tra 2026-10-22:
- [WebView 13.15.0 API](https://github.com/react-native-webview/react-native-webview/blob/v13.15.0/docs/Reference.md): Android first-load hook limitation, native navigation/permission/process APIs.
- [Android WebChromeClient source 13.15.0](https://github.com/react-native-webview/react-native-webview/blob/v13.15.0/android/src/main/java/com/reactnativecommunity/webview/RNCWebChromeClient.java): audit permission/file chooser behavior before enabling adapter.
- [Expo WebBrowser](https://docs.expo.dev/versions/latest/sdk/webbrowser/): system auth sessions, callback/cookie distinctions. Versioned SDK54 web URLs redirected to latest during audit; version pin comes from installed Expo manifest, not latest website recommendations.

Gate implementation tại mục XXIV đã được đáp ứng ngày 2026-10-22. Native SSO P0
và các thay đổi trust boundary chưa chốt vẫn giữ gate riêng như mô tả bên trên.
