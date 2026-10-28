# ANKT Developer Platform — Stage 7 specification

Status: **COMPLETE — LOCAL ACCEPTANCE** (2026-10-22). Implementation được cho phép
ngày 2026-10-21; Tasks 1–53 và Public/Confidential A–D đã xác minh trong fixture cô lập.
Xem [acceptance và giới hạn](../handoffs/developer-platform-stage-7-acceptance.md).
Rollout production vẫn cần phê duyệt riêng; giữ nguyên các lựa chọn bên dưới.
Nguồn yêu cầu: “Giai đoạn 7 — Mini App Review, Admin Moderation & Production Publishing”,
mục I–XXVI do người dùng cung cấp. [Kế hoạch](../plans/developer-platform-stage-7.md).

## 1. Mục tiêu và phạm vi

Developer tạo version, gửi xét duyệt và theo dõi quyết định; Admin tiếp nhận,
yêu cầu sửa/từ chối/phê duyệt; chủ ứng dụng phát hành **đúng version được duyệt**.
Admin tạm ngưng/khôi phục có kiểm soát. Mọi thay đổi có authorization, concurrency,
audit, notification và test. Không tự động publish khi approve.

Trong phạm vi: backend .NET 8/OpenIddict/EF PostgreSQL hiện tại; Developer Portal
Expo Web trong `viora`; Admin Portal React/Vite trong `viora-admin`; fixture isolated.
Không có marketplace, danh sách Tiện ích công khai, native runtime, Stage 8,
deploy production, gửi thư thật trong test hay migration vào DB ứng dụng.
“Publish” ở đây là nghiệp vụ trong DB/API; không phải deploy website đối tác.

## 2. Assumptions và quyết định cần duyệt

Các lựa chọn sau được giữ nguyên khi người dùng cho phép thực hiện Stage 7 ngày
2026-10-21; thay đổi policy/trust boundary đáng kể vẫn cần review lại:

1. Submission cần đủ bằng chứng thật theo Stage 6, mới trong 5 phút tại thời điểm
   commit. Đóng băng một attestation an toàn gắn snapshot hash để review không mất
   bằng chứng khi phiên Developer hết hạn hoặc retention xóa run. Approve/publish
   kiểm tra lại các điều kiện an toàn đang sống trên **snapshot đích**; không dùng
   lại readiness của draft mới và không tuyên bố proof cũ là quan sát mới. Không đặt
   thời hạn review tự động trong bản này; nếu cần TTL/retest, phải chốt trước code.
2. Một working draft/app và tối đa một review đang PendingReview/InReview/app.
   Developer vẫn sửa draft mới trong lúc version cũ được review. Resubmit sau
   ChangesRequested/Rejected luôn tạo version code mới và cần readiness mới.
3. Tất cả Admin hiện hữu có named policies của module; Moderator/User/Developer
   không có quyền review. Không tạo role Reviewer/SuperAdmin hoặc màn hình cấp quyền.
   Chỉ người được assign được quyết định; reassignment cần quyền, lý do và audit.
4. Restore chỉ gỡ operational suspension, **không tự publish lại và không hồi sinh
   token/consent đã thu hồi**. Owner phải publish một version Approved còn an toàn.
5. Draft đổi metadata/domain/callback/scopes không thay runtime Production. Giữ
   proof của hostname đang được review/publish và giữ asset đang được version pin.
   Revoke/expiry/đánh dấu không an toàn là sự kiện riêng, chặn runtime ngay theo policy.
6. Inbox Developer là thông báo bắt buộc, ghi cùng transaction. Email dùng SMTP
   hiện có nếu được cấu hình, gửi sau commit qua outbox; thiếu SMTP không tạo thông
   báo giả và không chặn inbox. Không yêu cầu tài khoản social tương ứng.
7. Không thêm upload attachments: dùng evidence/diagnostics an toàn hiện có và
   summary bất biến. Có thể mở rộng attachment sau bằng một yêu cầu riêng.

## 3. Audit dependency từ mã nguồn

Audit current working tree, không chỉ dựa vào nhãn “Stage 5/6 hoàn thành”. BE/FE có
thay đổi chưa commit từ các giai đoạn trước và công việc khác; giữ nguyên chúng.
Admin Portal sạch trước audit. Chưa có file Stage 7 hay implementation review.

| # | Hạng mục | Mã nguồn / phát hiện | Kết luận |
| --- | --- | --- | --- |
| 1 | MiniApps/entities | `Viora.Domain/Developers/MiniAppEntities.cs`; metadata, client, secret, redirect, domain, audit, logo. Chưa có version/review/publication. | Thêm schema, không coi enum là workflow đã tồn tại. |
| 2 | DeveloperAccounts | `DeveloperEntities.cs`, `DeveloperAuthentication.cs`; identity/session độc lập social, trạng thái Active/Suspended. | Tái sử dụng DeveloperActive và kiểm tra source session sống. |
| 3 | Status | `MiniAppStatus` hiện gộp Draft…Published/Suspended; `MiniAppService.Update` ghi metadata chung. | Tách các chiều; legacy status chỉ là compatibility projection. |
| 4 | Domain | `MiniAppDomainConfiguration.cs` chỉ cho một proof chưa revoke/app; `ChangeWebsite`/regenerate revoke proof cũ. | Phải tách lookup theo hostname và giữ proof đích review/publication. |
| 5 | SSO registry | `MiniAppSsoService.Save` ghi thẳng Production client/redirect và `app.SsoEnabled`; `SsoClientPolicy` chỉ yêu cầu legacy Published. | Chưa có approved-publication gate; phải sửa trước khi mở publish. |
| 6 | Sandbox launch | Session/grant/runtime/host có ownership, source, nonce, expiry, snapshot và separate executor cookie. | Tái sử dụng; không mở rộng quyền tester vì quyền review. |
| 7 | Readiness | `MiniAppSandboxConfigurationService.ReadinessAsync`, `MiniAppSandboxEvidenceReader`; 18 checks, newest completed run, live fingerprint, 5 phút. | Thật nhưng gắn working configuration/session; cần attestation lúc submit. |
| 8 | SDK/Bridge | `../viora/packages/miniapp-sdk`, bridge v1/v2, Public PKCE/popup và Confidential backend exchange. | Không tạo SDK hay OAuth server mới. |
| 9 | Admin authentication | `Program.cs`, `JwtTokenService.cs`; JWT access có sub/sid/role; Admin Portal dùng account login/refresh. | Dùng bearer hiện tại, không dùng Developer token ở Admin API. |
| 10 | Admin authorization | `[Authorize(Roles="2")]`, `AccountRole`: User=0, Moderator=1, Admin=2; chưa có permission registry. | Named module policies + live account/role/session checks; không tin role từ FE. |
| 11 | Notifications | `Notification.UserId` FK social User; `NotificationService.SendAsync` save rồi publish. Developer không có FK social. `IDeveloperEmailSender`/SMTP đã có. | Không ép Developer ID thành User ID; thêm inbox/outbox có recipient Developer. |
| 12 | Audit | `MiniAppAuditLog.ActorDeveloperId`; `AdminLog.AdminId` FK social User. | Review action lưu actor identity rõ ràng; mirror AdminLog nếu có User hợp lệ. |
| 13 | Migrations | Latest `20261020065352_AddSandboxExecutorLaunchBinding`; model Stage 6 khớp migration theo check đã chạy. | Additive migrations, PG rollback/race tests; chưa chạy migration ứng dụng. |
| 14 | Admin Portal | `../viora-admin/src/App.tsx`, `AdminLayout`, Axios/React Query; chưa có Mini App module. | Mở rộng Portal hiện có. Build audit thành công; warning chunk >500 kB có sẵn. |
| 15 | Developer Portal | `MiniAppDetail` có tab “Kiểm duyệt” placeholder; ConsoleGuard/BFF/unsaved-change dialog đã có. | Thay placeholder bằng “Kiểm duyệt & Phát hành”, giữ route/guard. |
| 16 | API convention | Developer typed DTO + `{error:{code,message}}`, no-store, BFF allowlist/Origin/CSRF; Admin response adapter riêng. | Giữ convention từng boundary, thêm typed contracts và stable errors. |
| 17 | Test infrastructure | xUnit/WebApplicationFactory, `IsolatedPostgres`, Node/tsx, HTTPS Chrome/CDP fixtures; Admin chưa có npm test script. | Reuse infra; thêm targeted Admin tests và 4 E2E flow thật sau duyệt. |

Điểm bổ sung: `MiniAppLogoStorage` xóa upload detached/expired; version phải pin
managed logo để thay logo draft không làm mất logo đã gửi duyệt/đang publish.

Bằng chứng baseline cùng workspace ngày 2026-10-20, đã đối chiếu artifact với source:
backend **487 pass / 1 MoneyAudit skip có sẵn**, SDK **37**, host/executor **23**,
FE/BFF **42**; .NET8 Release, EF model, FE types/lint/export và browser Stage 6 pass.
Đây là kết quả Stage 6 vừa chạy, **không phải test Stage 7**. Xem
[acceptance Stage 6](../handoffs/developer-platform-stage-6-acceptance.md).
Audit này chạy thêm Admin `npm run build`: pass, log
`.codex-tmp/stage7-admin-preflight-build.log`. Không lặp lại suite đã pass khi source
không đổi; implementation sẽ có RED/GREEN và regression riêng.

Dependency gate: có thể thiết kế/triển khai phần độc lập sau approval. Submit bị chặn
nếu Sandbox/executor không bật hoặc thiếu proof thật. Approve/publish bị chặn nếu
thiếu live domain/client/security checks. Không seed approval cho dữ liệu thật, không
dùng legacy Published hay projection OpenIddict làm bằng chứng đã duyệt. Existing
Production rows không có publication được duyệt phải fail closed sau rollout Stage 7;
không backfill phê duyệt tự động. Public DNS/TLS/deployed headers vẫn là rollout gate.

## 4. Kiến trúc và cấu trúc project

Giữ Domain/Application/Infrastructure và API host hiện có. Không tạo database,
OAuth server, Portal hay notification provider khác.

- `Viora.Domain/Developers`: version/review/publication/operational/inbox entities.
- `Viora.Application/Developers`: explicit version, review, publishing DTO/contracts.
- `Viora.Infrastructure/Developers`: `MiniAppVersionService`, `MiniAppReviewService`
  (read/query), `MiniAppReviewSubmissionService`, `MiniAppReviewDecisionService`,
  `MiniAppPublishingService`, `MiniAppReadinessService`, `MiniAppModerationService`,
  `MiniAppReviewNotificationService`, `MiniAppReviewAuditService`. Access/snapshot
  policy helpers khi cần; không tạo hai service thực hiện cùng một quyết định.
- `Viora.Infrastructure/Sso`: reader/registry/consent/runtime dùng publication target
  cho Production, giữ Sandbox path hiện tại. Dùng OpenIddict managers cho revocation.
- `Viora.Infrastructure/Persistence/{Configurations,Migrations}`: mappings/invariants.
- `viora-BE/Developer`, `viora-BE/Admin`: management endpoints và scoped authorization.
- `../viora/features/developer/{components,lib,tests}`, `server/developer`: review UI,
  inbox, typed client, BFF allowlist; giữ Expo Web.
- `../viora-admin/src/{pages,components,services,types}`: module Mini App trong layout
  hiện có; Axios/React Query/router hiện hữu.
- `Viora.Application.Tests`, `tools/SsoE2EHost`, `tools/MiniAppReviewE2E`: tests/fixture.

Style: constructor injection, explicit DTOs, `TimeProvider`, async cancellation;
GUID concurrency riêng với số version nghiệp vụ; entities không trả trực tiếp ra API.
Ví dụ contract dự kiến, chưa implement:

```csharp
public sealed record ReviewDecisionRequest(Guid RequestId, Guid ConcurrencyToken, string? Reason);
public sealed record ReviewDecisionDto(Guid ReviewId, string Status, Guid ConcurrencyToken, DateTime DecidedAt);
```

## 5. Data model và invariants

| Bảng / thay đổi | Nội dung |
| --- | --- |
| MiniAppVersions | Id, MiniAppId, VersionCode (server cấp tăng dần), VersionName, State Draft/Submitted/Archived, SnapshotSchemaVersion, SnapshotJson, SnapshotHash, CreatedByDeveloperId, CreatedAt/UpdatedAt, SubmittedAt, ConcurrencyToken; nullable LogoUploadId để pin asset. |
| MiniAppReviewRequests | Id, MiniAppId, MiniAppVersionId, DeveloperAccountId, Status, SubmittedAt, AssignedAdminAccountId, ReviewStartedAt, DecidedAt, DecisionReason, bounded ChangeRequestsJson, sealed EvidenceAttestationJson, CorrelationId, ConcurrencyToken. |
| MiniAppReviewActions | Append-only actor DeveloperAccountId hoặc AdminAccountId, ActorType, action/target, before/after, reason, server timestamp/correlation, safe typed metadata; RequestId + payload hash + safe result cho idempotency. |
| MiniAppPublications | Id, MiniAppId, MiniAppVersionId, ApprovedReviewRequestId, ProductionOAuthClientId (nullable chỉ khi snapshot tắt SSO), Environment=Production, Status, PublishedAt, UnpublishedAt, PublishedByDeveloperId, SuspendedAt/reason khi applicable, ConcurrencyToken. Version association bất biến mỗi publication. |
| MiniAppOperationalStates | Một row/app; Active/Suspended, reason, actor/time, revision. Authoritative suspension độc lập draft/review/publication. |
| MiniAppReviewNotifications | Id, RecipientDeveloperId, EventId/type, MiniAppId, review/version/publication references, CreatedAt/ReadAt; server-derived deep link. |
| MiniAppReviewNotificationDeliveries | Transactional email outbox, event/recipient/channel unique, Pending/Processing/Sent/Failed, attempt/lease/next retry; không ghi secret hoặc SMTP credentials vào payload. |
| Domain/asset indexes | Unique active domain proof theo (MiniAppId,Hostname); tất cả consumer chọn hostname đích rõ ràng. Logo cleanup loại upload có version reference. |

Snapshot là typed canonical JSON <=64 KiB, schema version cố định, chỉ configuration:
name/logo/descriptions/category/website/domain/privacy/terms/support, SSO flag,
callback/scopes/client type/client identity và Sandbox/Production intent. Arrays
normalize/sort, URI so khớp exact theo Stage 5, hash SHA-256 do server tính.
Không có Client Secret, ciphertext, cookie, code/token, signing/private key, arbitrary
HTML hoặc credential envelope. SnapshotHash không phải authorization.

Working metadata vẫn được edit qua API hiện có và sync working draft trong cùng
transaction. Khi submitted, snapshot và version identity không sửa được bằng API
hay ordinary SQL UPDATE: PostgreSQL trigger + application validation. Sửa tiếp tạo
working version mới; GET historic version luôn đọc snapshot, không join metadata mới.
Chống đổi review-version/publication-version FK sau khi tạo; composite FK giữ cùng app.

Unique (app,versionCode), một Draft/app, một active review/app và active review/version,
một publication Published/app; indexes cho status/time/assignee/inbox/idempotency.
Concurrency token không nhận tùy ý từ FE để bypass. FK Restrict, UTC, bounded columns,
enum/check constraints; không physical-delete lịch sử qua endpoint nghiệp vụ.
Migrations tách schema/invariants/domain-index, không tự approve/publish legacy rows.
PG Up/Down/Up giữ nguyên auth, social, registry secrets, Sandbox và dữ liệu không liên quan;
Down cần cảnh báo mất riêng lịch sử Stage 7. Từ chối downgrade nếu còn effective
publication hoặc legacy Published có thể được binary cũ kích hoạt lại; yêu cầu
operator unpublish/quarantine rõ ràng trước, không âm thầm mở lại Production.

## 6. Bất biến draft / approved Production

Đây là thay đổi bắt buộc so với source Stage 5:

- Draft metadata/SSO intent không là runtime Production. Desired Production config
  nằm trong working version. Live client identity/secret store giữ ở registry hiện có;
  chỉ publishing transaction apply approved type/scopes/callbacks và projection.
- Direct write `/sso/Production` trả 409 `PRODUCTION_DRAFT_REQUIRED`; desired config
  được sửa bằng version-draft API, FE cập nhật cùng slice. Không cho endpoint cũ
  mutate live config để lách approval. GET giữ typed legacy fields; desired draft
  và published config hiển thị riêng. Sandbox configuration tiếp tục dùng path cũ.
- Candidate Confidential có thể prepare credential trong encrypted store qua owner
  command rõ ràng, khi thiếu credential hợp lệ. Không revoke/đổi credential đang chạy
  chỉ vì sửa draft; không trả secret trong review/admin DTO. Secret rotation riêng
  tiếp tục có confirmation/audit, không làm thay approved scopes/redirect/type.
  Trước first publish, validation kiểm tra candidate identity/config/credential,
  không yêu cầu Production authorization đã chạy (sẽ tạo vòng dependency). Projection
  được apply cùng publication; real Production exchange phải được kiểm thử sau đó.
  Khi snapshot tắt SSO, Production OAuth checks là inapplicable, không tạo client
  giả cho đủ checklist; mọi endpoint SSO của publication đó bị từ chối.
- Production `SsoClientReader` phải load actual current publication + Approved review
  + verified snapshot hash + live developer/operational/domain/client guards.
  `SsoClientPolicy`, origin checks, consent display và OpenIddict projection dùng
  approved fields/callbacks; `app.Status==Published` một mình không đủ quyền.
- Đổi website draft không revoke proof đang phục vụ snapshot được review/publish.
  Update tất cả truy vấn đang giả định một proof/app; không chỉ đổi unique index.
  Explicit revoke/regenerate/expiry vẫn fail closed cho hostname chịu ảnh hưởng.
- Managed logo còn được submitted/published version pin không bị cleanup xóa.
  Draft flags `SsoEnabled`, name/logo không thay consent/Production đang chạy.
- Publish version mới đóng publication trước và apply target atomically; không đọc
  “draft mới nhất” thay cho versionId người dùng xác nhận. Cấu hình bảo mật thay đổi
  phải thu hồi Production grants/tokens liên quan, không phục hồi chúng khi rollback.
- Website đối tác có thể đổi nội dung ngoài ANKT; snapshot chỉ bất biến cấu hình,
  không phải bảo đảm byte-for-byte deployment bên ngoài hay quét mọi mã JavaScript.

## 7. State machine và transaction

| Chiều | Transitions |
| --- | --- |
| Version | Draft -> Submitted (sealed); sửa sau đó tạo Draft mới, không unseal version. |
| Review | PendingReview -> InReview -> ChangesRequested / Rejected / Approved. Terminal decision không đổi. |
| Publication | Unpublished -> Published khi đúng Approved target; Published -> UnpublishedByOwner hoặc Archived khi thay version. |
| Operational | Active -> Suspended bởi Admin; active publication thành Suspended. Restore -> Active + Unpublished; owner publish lại sau validation. |

`MiniApp.Status` giữ để compatibility (draft/configuration/published/suspended projection),
không là nguồn quyết định review/publication. Review reject không tắt một publication
cũ còn an toàn; suspension thì chặn cả hai environment theo app guard.

Submit: authenticate owner/source -> bounded readiness/network preflight -> transaction
Serializable -> recheck owner, draft revision/hash, active review, proof/client versions,
same completed run/provenance/freshness -> seal version+attestation -> review/action/
notification/outbox -> commit. Không gửi SMTP hay giữ network I/O dài trong DB transaction.

Start/assign/decide kiểm tra live Admin và expected concurrency; chỉ assignee quyết định.
Approve revalidates sealed target/live security, không so với draft mới rồi overwrite
snapshot. Reject/changes cần reason 10–2000 ký tự; changes có <=20 item field-catalog/message
để UI link đúng section. Approval optional note; không có override core security gates.

Publish/unpublish/suspend/restore dùng app/publication concurrency và target version
rõ ràng. Recheck cuối transaction sau preflight; unique constraints/conditional update
cho đúng một winner. Conflict 409; retry cùng RequestId sau timeout trả safe original
result, không duplicate action/inbox/outbox. Cùng RequestId khác body/target ->409.
Replay vẫn phải qua auth/ownership sống; retry DB không tự gửi side effects bên ngoài.

Publication wire contract: readiness requires exactly one non-empty GUID `versionId`.
Publish sends `requestId`, `versionId`, `concurrencyToken` (app revision),
`publicationId`/`publicationRevision` (both null for no active publication), and
`operationalRevision` (null only before initialization). Unpublish sends
`requestId`, `concurrencyToken` (app), exact `publicationId`/`publicationRevision`,
`operationalRevision`, and optional `reason` (up to 2000 characters). Mutation
responses contain `publication`, `appVersion`, and `operationalRevision`; clients
must retain the original payload/request ID for a retry, or reread readiness for
a new command. Old command replay returns its original receipt even after a later
publication; it never reopens or closes another publication.

Moderation sends `requestId`, `concurrencyToken` (app), `operationalRevision`
(null before first initialization), current `publicationId`/`publicationRevision`
(both null when unpublished), and required `reason` (10–2000 characters).
Restore requires the current Suspended operational revision and no active
publication. Responses return `operational`, `appVersion`, and the publication
closed by suspension, if any; restore returns no live publication.
Inbox uses `page`/`pageSize` and optional exact `unread=true|false`, returning
`items`, `unreadCount`, `page`, `pageSize`, and `total`. Mark-read posts `{}`;
unknown recipient/actor fields are rejected. Lists/counts include only receipts
for the signed-in recipient's currently owned, non-deleted apps. Read times
never move backward. Deep links remain under `/developers/console/apps/` with
server-generated review/version/publication identifiers, never partner URLs.

Suspend thu hồi/vô hiệu app-specific OIDC tokens/authorizations và Sandbox runtime
theo policy, chặn authorize/code exchange/UserInfo/introspection kể cả token cũ.
Không thu hồi social/Admin/Developer sessions hoặc token Mini App khác. Restore không
bỏ qua domain/client/developer guard; không có quyền owner tự bỏ suspension.

## 8. API contracts dự kiến

Developer dùng DeveloperActive/BFF hiện có, không nhận actor/status/evidence/env từ body.
Mỗi mutation có RequestId và expected revision phù hợp; owner của nested resource
được xác minh trên server, không chỉ check appId ở route.

| Method / path | Input / output |
| --- | --- |
| GET /api/developer/mini-apps/{appId}/versions | Bounded history, immutable version summaries; page/pageSize. |
| GET /api/developer/mini-apps/{appId}/versions/{versionId} | Owned snapshot DTO + hash/state/revision, không secret. |
| POST /api/developer/mini-apps/{appId}/versions | Create working draft from current working config; requestId/versionName/app revision. |
| PUT /api/developer/mini-apps/{appId}/versions/{versionId} | Chỉ Draft: name/desired Production config; strict DTO/revision. Existing metadata editor sync cùng transaction. |
| POST /api/developer/mini-apps/{appId}/versions/{versionId}/production-credentials | Owner-only candidate Confidential preparation khi cần; sensitive response no-store, explicit action; không đưa vào review payload. |
| GET /api/developer/mini-apps/{appId}/review-readiness | readyForReview, requiredChecks, warnings, blockingReasons, evaluatedAt, version/hash/revisions; final server authority. |
| POST /api/developer/mini-apps/{appId}/reviews | requestId/versionId/revisions; sealed submission, 201 hoặc replay result. |
| GET /api/developer/mini-apps/{appId}/reviews[/{reviewId}] | Bounded owned history/detail/actions/change items/evidence summary. |
| GET /api/developer/mini-apps/{appId}/publication-readiness | versionId required; security/publish checklist + capabilities, không dùng latest draft ngầm. |
| GET /api/developer/mini-apps/{appId}/publications | History + effective operational/publication state. |
| POST /api/developer/mini-apps/{appId}/publish | requestId/approved versionId/expected app+publication revisions. |
| POST /api/developer/mini-apps/{appId}/unpublish | requestId/target publicationId/revision, optional reason. |
| GET /api/developer/review-notifications | page/pageSize/unread filter; recipient derived, link server-generated. |
| POST /api/developer/review-notifications/{id}/read | Owned idempotent mark-read. |

| Admin path (prefix /api/admin) | Behavior |
| --- | --- |
| GET /mini-app-reviews/overview | Aggregate counts cho queue/review/publication/suspension. |
| GET /mini-app-reviews | Search, status, assignee, page/pageSize, allowlisted sort + stable ID. |
| GET /mini-app-reviews/{reviewId} | Snapshot/developer contact/domain/config/evidence/history/capabilities; no secret. |
| POST /mini-app-reviews/{reviewId}/start | Atomically claim PendingReview -> InReview. |
| POST /mini-app-reviews/{reviewId}/assign | Explicit reassignment InReview, target live Admin, required reason. |
| POST /mini-app-reviews/{reviewId}/approve | assignee + review revision + live target security validation. |
| POST /mini-app-reviews/{reviewId}/request-changes | Reason + bounded structured change items. |
| POST /mini-app-reviews/{reviewId}/reject | Required reason, no publish. |
| GET /mini-apps[/{appId}] | Safe version/publication/operational detail, filtered lists. |
| POST /mini-apps/{appId}/suspend | Expected revision, required reason/category, atomic audit/revocation/notification. |
| POST /mini-apps/{appId}/restore | Expected revision, required reason, live safety rechecks; stays unpublished. |

List pageSize default20/max100, keyword<=128, bounded reasons/JSON, finite sort/status
catalog, UTC ISO timestamps. Không arbitrary evidence/status/actor/snapshot update.
Body DTO JSON max64 KiB, command thường <=16 KiB; readiness timeout giữ budget Stage 6.
Errors có code ổn định + message an toàn + typed blockers/field keys khi applicable:
400 validation, 401 no session, 403 permission/policy, 404 nonowned/notfound,
409 concurrency/duplicate/tamper/stale, 429 quota, 503 missing dependency, 504 timeout.
Developer giữ envelope hiện có; Admin dùng adapter `AdminApiResponse` và structured
error.code tương thích `getErrorMessage`. No-store cho dữ liệu management/credentials.

## 9. Authorization, notifications và audit

Named policies cho Read/Decide/Suspend của Mini App map role Admin=2 hiện hữu, kiểm
tra live Account active/not deleted, role và signed sid có live RefreshToken session.
Không dùng localStorage role hay chỉ signed-but-stale role claim làm quyền cuối cùng.
Admin API không chấp nhận DeveloperSession; Developer API không nhận Admin Bearer.
Không thay auth login/global role flow; không thay CORS nếu audit chưa chứng minh cần.
Admin Sandbox launch vẫn cần explicit Stage 6 tester allowlist; review read không
ngầm cấp quyền runtime hay executor.

Developer BFF chỉ thêm exact owned version/review/publication/inbox routes/methods;
không passthrough admin/runtime/internal executor. Giữ Origin + mutation CSRF/header
checks, cookies, trusted attribution và redirects=error. Admin bearer mutations
có scoped origin/custom-header checks khi có browser Origin; không nhận credential
từ query/cookie thay bearer. Default module limits theo actor: reads120/phút,
mutations30/phút, credential preparation5/phút; cấu hình bounded, có tests 429.

Inbox event: Submitted, Started, ChangesRequested, Rejected, Approved, Published,
Unpublished, Suspended, Restored. Một event/recipient đúng một row khi retry.
Admin nhận công việc qua queue/dashboard; chưa broadcast/email tất cả Admin.
Email worker batch<=100, claim lease, retry tối đa5 với backoff, dừng ở Failed cho
vận hành kiểm tra. Exactly-once inbox; email là at-least-once/best-effort khi SMTP
đã nhận nhưng ACK bị mất, không tuyên bố exactly-once qua nhà cung cấp. Không log body.

Audit gồm actor identity/type, target/version/hash, previous/new state, reason,
UTC/correlation, safe fixed metadata; append-only action cùng transaction. Không
nhồi AdminAccountId vào FK User của AdminLog, không ghi secret/cookie/token/full HTTP
payload. Lịch sử/read DTO luôn allowlist, reason render escaped plain text.

## 10. UX/UI

Developer tab **Kiểm duyệt & Phát hành**: tách working draft / submitted version /
current Production; checklist do BE quyết định, blockers link đúng cấu hình, version
name/code, history/changes/reason, retry/cancel confirmation, publish đúng target và
unpublish. Pending disables duplicate submit; Changed/Rejected cho sửa draft mới.
Hiển thị rõ “Approved chưa phát hành”, “Draft mới không thay Production” và trạng thái
suspension. Inbox có unread/read + đúng Mini App deep link, không phụ thuộc social login.

Admin module **Quản lý Mini App**: overview và các view queue/history/approved/
published/suspended chia sẻ list filter/search/sort/pagination. Detail dùng nhóm
thông tin ứng dụng, Developer, website/legal, integration, test/evidence, timeline
và action rail; không dump bảng DB/JSON/Client Secret. Modal xác nhận mọi decision;
reject/changes/assign/suspend/restore yêu cầu reason rõ. Server capabilities chỉ hỗ
trợ hiển thị, server vẫn kiểm tra lại mọi mutation. Refresh khi conflict giữ context.

Giữ design system/layout hiện tại của từng Portal. Loading/empty/error/permission/
timeout/success, keyboard/focus/announcements, responsive 320/375/768/desktop; tables
scroll trong vùng riêng. Không nhúng trang đối tác vào privileged Admin document;
launch Sandbox chỉ trên isolated host/route được phép, external link không opener.

## 11. Commands và testing

Từ backend root, sau approval, restore từ cấu hình dependency hiện có nếu cần:

```powershell
dotnet build viora-BE.sln --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --filter 'FullyQualifiedName~MiniAppReview|FullyQualifiedName~MiniAppPublishing|FullyQualifiedName~MiniAppModeration' --disable-build-servers -m:1 -p:UseSharedCompilation=false
node --test --experimental-test-isolation=none tools/SandboxHostTests/*.test.mjs tools/SandboxExecutor/protocol.test.mjs
dotnet build tools/SsoE2EHost --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
# Sau khi Task E2E tạo runner này (chưa tồn tại ở checkpoint spec):
node tools/MiniAppReviewE2E/run-fixture.mjs
```

EF offline dùng `VIORA_EF_DESIGN_CONNECTION` synthetic/unreachable theo Stage 6,
không đọc appsettings chứa connection ứng dụng để test migration. Chạy actual
migrations/races chỉ qua `IsolatedPostgres`; không `database update` ứng dụng.

```powershell
# FE root
node --import tsx --test --experimental-test-isolation=none server/developer/*.test.cjs features/developer/tests/*.test.ts
npx tsc --noEmit
npm run build:web
npm --prefix packages/miniapp-sdk test
# Admin root
npm run build
npm run lint
# Sau khi authored Admin tests được tạo, không gọi trước rồi coi là pass:
node --test --experimental-test-isolation=none src/services/admin-mini-app*.test.mjs src/components/MiniAppReview/*.test.mjs
```

TDD cho 22 backend cases của yêu cầu: happy/blocked/double/foreign submit,
immutable snapshot, start/approve/reject/changes/no reason, insufficient role,
developer self-approval, dual Admin race, approved-only exact-version publish,
draft substitution denial, suspend/restore, issuance denied after suspension,
transactional audit/notifications, PG concurrency và no-secret DTOs.
Thêm retention/expiry, stale proof/client, partial failure/retry, legacy Published
bypass, draft domain/logo/client mutations, obsolete reviewer và cross-environment.

Frontend: các trạng thái/decision modal/checklist/list/detail/permission và error
theo mục XXII; real browser verification trên cả hai Portal, network/console,
focus/mobile, XSS reasons, không chỉ snapshot test hoặc mocked fetch là E2E.

E2E A: actual Developer login -> verified configuration + real Stage 6 checks ->
create/submit version -> actual Admin login/start/detail/approve -> Developer inbox
-> publish exact target -> real Production OIDC consent/code/token/UserInfo bằng
Production client; Sandbox token không dùng được ở resource Production.
B: request changes -> Developer đọc từng item -> new version + fresh checks ->
resubmit/approve. C: reject -> reason/inbox -> rejected-version publish denied.
D: Published -> Admin suspend -> authorize, code/token/UserInfo/introspection denied
-> inbox -> restore stays unpublished -> explicit validated republish -> new login.
Không trực tiếp seed Approved/Published để thay thế các bước nghiệp vụ trong E2E.
Local identities/domain/SMTP transport có thể seed rõ ràng; không dùng DB thật.

## 12. Boundaries, success criteria và human gate

Always: đọc source, giữ unrelated edits/auth flows, explicit ownership/policies,
bounded pagination, immutable target, transactions, tests RED/GREEN, review diff,
checkpoint và handoff kỹ thuật sau mỗi nhóm lớn.
Approval spec bao gồm schema additive và policy/contract thay đổi nêu rõ ở trên;
không xin lại approval cho mỗi file/task đã duyệt. Thay trust model/permission,
notification audience, dependency/provider hoặc semantics freeze/publish phải cập nhật
spec và review lại. Không deploy, application DB migration, gửi mail người thật,
marketplace, Stage 8, fake test evidence hoặc tự duyệt Mini App thật.

Hoàn thành khi 4 flows thật, 22 backend/security cases, 2 Portal states, PG migration/
concurrency, SDK/SSO/Sandbox regressions, build/types/targeted lint đều có bằng chứng;
không có approval/publish bypass; lịch sử/notification atomic; final handoff đủ 17
nhóm mục XXVI, limitations rõ và điều kiện Stage 8, không tự bắt đầu Stage 8.

**Cần human duyệt:** 7 assumptions mục 2; cách tách draft/runtime và domain/asset giữ
version mục 6; legacy Production fail-closed; một reviewer-assignee và reassignment;
sealed evidence tại submit + live checks tại approve/publish thay vì ép 5-minute TTL
suốt thời gian review; restore không tự republish; schema/API/task order.
Theo mục XXIV người dùng: **“Chỉ code sau khi được phê duyệt.”** Gate implementation
đã được đáp ứng bằng yêu cầu triển khai ngày 2026-10-21 và tiếp tục hoàn thành ngày
2026-10-22. Gate này không cấp phép deploy, migration DB ứng dụng hay Stage 8.
