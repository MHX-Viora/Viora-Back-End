# ANKT Developer Platform — Stage 7 implementation plan

Status: **COMPLETE — LOCAL ACCEPTANCE** (2026-10-22). Tasks 1–53 hoàn tất theo
spec/plan đã được phép triển khai ngày 2026-10-21. Bằng chứng và giới hạn ghi tại
[acceptance](../handoffs/developer-platform-stage-7-acceptance.md) và
[handoff](../handoffs/developer-platform-stage-7.md).
[Specification](../specs/developer-platform-stage-7.md). Scope chỉ Stage 7, không marketplace/Stage 8.

Final checkpoints A–F: backend 695 passed / 1 existing unrelated skip; Developer/BFF
63, Admin 18, SDK 37 and host/executor 23 passed. Release/model/types/lint and Portal
builds passed. Actual Developer BFF login, Admin password/live-session login and
Public/Confidential A–D browser workflows passed on isolated PostgreSQL/Chrome;
both runners exited 0 after cleanup. No application database migration was run.
Local acceptance does not authorize production rollout. Keep the default flag off.

## Pre-flight đã làm

- [x] Đọc đầy đủ yêu cầu, đặc biệt human gate mục XXIV và boundaries mục XXV.
- [x] Audit 17 dependency qua source; bảng bằng chứng ở spec mục 3.
- [x] Đối chiếu Stage 5/6 với code và artifact vừa chạy: 487 backend pass/1 existing
  skip; SDK37, host/executor23, FE/BFF42; Release/model/types/lint/browser đã có bằng chứng.
- [x] Build riêng Admin hiện có: pass; warning chunk >500 kB có sẵn.
- [x] Phát hiện gaps: không version/review/publication; Production đọc mutable draft;
  app-only domain proof uniqueness; logo cleanup; notification/audit FK social.
- [x] Tạo đúng spec/plan. Không implement, migrate DB ứng dụng, deploy hay gửi mail thật.
- [x] Người dùng cho phép thực hiện Stage 7 ngày 2026-10-21; giữ assumptions/policy/schema/API/task order trong spec, không cần hỏi lại cho các task đã mô tả.

Các lựa chọn cần duyệt: sealed submission evidence + live safety rechecks, tối đa
một active review/app, current Admin policies/assignment, draft/Production isolation,
domain/asset retention, restore không republish, Developer inbox + optional email.
Production legacy không có approved publication sẽ fail closed, không auto approve.

## Execution rules

Mỗi task theo thứ tự dependency: đọc spec/source liên quan -> failing test có ý nghĩa
-> thay đổi nhỏ -> GREEN -> build/targeted tests -> review diff -> checkpoint.
Không tạo hàng loạt code trước khi verify. Khoảng <=5 authored files/task; nếu vượt,
tách slice trước code. EF generated migration/Designer/model snapshot được ghi riêng
trong checkpoint, không dùng ngoại lệ generated để che authored rewrite lớn.

Giữ dirty changes BE/FE hiện có; Admin chỉ sửa module liên quan. Không tự commit,
stage file không liên quan, cài package hoặc thay CORS/auth login/global roles.
Không tự gọi mail/social notification ra ngoài; fixtures dùng recipient/test transport
isolated. Approval tài liệu cho phép các additive schema/task đã mô tả, không cần
hỏi lại cho từng sửa đổi routine. Trust/policy/dependency thay đổi đáng kể cần review lại.

Đường dẫn viết tắt trong bảng: D=`Viora.Domain/Developers`, A=`Viora.Application/Developers`,
I=`Viora.Infrastructure/Developers`, P=`Viora.Infrastructure/Persistence`,
S=`Viora.Infrastructure/Sso`, H=`viora-BE`, T=`Viora.Application.Tests`,
F=`../viora/features/developer`, B=`../viora/server/developer`, AD=`../viora-admin/src`.
Các đường dẫn trong bảng là dự kiến ban đầu; file thực tế và bằng chứng cuối cùng
được ghi trong handoff/acceptance.

## Tasks 1–8 — persistence và contracts

Historical checkpoint 2026-10-21 after Tasks **1–13** (superseded by final acceptance): persistence, canonical snapshot,
live authorization, version service, owned API/BFF and legacy-editor draft sync.
Task 12 was split into backend wiring/fixture and BFF slices; Task 13 includes
integration/redirect hooks plus transient concurrency handling. Verification and
commands are recorded in [handoff](../handoffs/developer-platform-stage-7.md).
At that checkpoint Tasks **14–53 remained**; all are now complete locally.
`MiniAppReview:Enabled` still defaults false pending separately approved rollout.

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 1 | Version schema/contracts. Gate: approval. | D/MiniAppVersionEntities.cs; A/MiniAppVersionContracts.cs; P/Configurations/MiniAppVersionConfigurations.cs; P/AppDbContext.cs; T/MiniAppVersionPersistenceTests.cs | Draft/version code/revision/asset pin, unique app+code và one draft; RED/GREEN mapping/constraint tests. |
| 2 | Version migration + DB immutability. Depends 1. | Migration authored Up/Down/SQL; T/MiniAppVersionMigrationTests.cs (+ EF generated files) | PG Up/Down/Up, submitted configuration/identity immutable, FK no cross-app; existing auth/client rows preserved. |
| 3 | Review schema/contracts. Depends 1. | D/MiniAppReviewEntities.cs; A/MiniAppReviewContracts.cs; P/Configurations/MiniAppReviewConfigurations.cs; P/AppDbContext.cs; T/MiniAppReviewPersistenceTests.cs | Request states, immutable target, append-only action, evidence attestation and structured change items bounded. |
| 4 | Review indexes/migration. Depends 2,3. | Migration authored constraints/triggers; T/MiniAppReviewMigrationTests.cs (+ generated) | One active review/app+version; actor/idempotency uniqueness; terminal decision conflict; PG tamper/rollback tests. |
| 5 | Publication/operational schema. Depends 3. | D/MiniAppPublicationEntities.cs; A/MiniAppPublishingContracts.cs; P/Configurations/MiniAppPublicationConfigurations.cs; P/AppDbContext.cs; T/MiniAppPublicationPersistenceTests.cs | Separate dimensions; exact approved target + same-app refs; one effective published row; no legacy auto approval. |
| 6 | Publication migration. Depends 4,5. | Migration authored schema/checks; T/MiniAppPublicationMigrationTests.cs (+ generated) | PG rollback/Up, ordinary grants/secrets unaffected; Down refuses potentially active/legacy Published state until operator explicitly unpublishes/quarantines. |
| 7 | Developer inbox/outbox schema. Depends 3,5. | D/MiniAppReviewNotificationEntities.cs; A/MiniAppReviewNotificationContracts.cs; P/Configurations/MiniAppReviewNotificationConfigurations.cs; P/AppDbContext.cs; T/MiniAppReviewNotificationPersistenceTests.cs | Developer recipient FK, unique event/recipient/channel, lease/attempt caps, no social identity fabrication. |
| 8 | Inbox/outbox migration. Depends 6,7. | Migration authored schema/indexes; T/MiniAppReviewNotificationMigrationTests.cs (+ generated) | PG Up/Down/Up and idempotency constraints, application DB never contacted. |

Checkpoint A: new model/build/focused tests and migration chain pass; no workflow
or publish endpoint enabled before its guards. If schema conflicts with actual data,
stop affected task and report; do not seed approvals or drop constraints to proceed.

## Tasks 9–20 — boundaries, draft integrity và runtime foundation

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 9 | Scoped actor authorization. Depends 5. | I/MiniAppReviewAccess.cs; H/Admin/MiniAppReviewAuthorization.cs; H/Developer/MiniAppReviewRegistration.cs; T/MiniAppReviewAccessTests.cs; T/MiniAppReviewAuthEndpointTests.cs | Real Admin=2, live signed sid/account/session; no Developer/User/Moderator/stale role; owner IDOR 404. Existing login untouched. |
| 10 | Typed canonical snapshot. Depends 1. | A/MiniAppVersionSnapshot.cs; I/MiniAppVersionSnapshotPolicy.cs; T/MiniAppVersionSnapshotTests.cs | Deterministic schema/hash/URI/scopes, <=64 KiB, no credentials/private payload; recompute rejects tamper. |
| 11 | Working draft/version service. Depends 9,10. | I/MiniAppVersionService.cs; A/MiniAppVersionContracts.cs; T/MiniAppVersionTests.cs; T/MiniAppVersionPostgresTests.cs | Server version allocation, one working draft, submitted immutable, next edits create new version, concurrency/replay. |
| 12 | Owned version API/BFF. Depends 11. | H/Developer/MiniAppVersionApi.cs; H/Developer/MiniAppReviewRegistration.cs; B/account-proxy.cjs; B/version-proxy.test.cjs; T/MiniAppVersionEndpointTests.cs | Exact approved version-route catalog, kể cả transport rule cho credential preparation sau Task 20; bounded history, revision+requestId, no admin/runtime passthrough. Unmapped backend route vẫn 404. |
| 13 | Draft mutation hooks + close direct Production writes. Depends 11,12. | I/MiniAppService.cs; I/MiniAppSsoService.cs; I/MiniAppVersionService.cs; T/MiniAppVersionMutationTests.cs; T/MiniAppSsoConfigurationTests.cs | Root metadata/Sandbox changes sync draft atomically; `/sso/Production` refuses live mutation with explicit 409. Prepared intent never changes published config. |
| 14 | Hostname-scoped domain indexes/lifecycle. Depends 11. | P/Configurations/MiniAppDomainConfiguration.cs; I/MiniAppDomainLifecycle.cs; T/MiniAppVersionDomainTests.cs; migration (+ generated) | Preserve proof pinned by pending/published target, new draft host gets independent proof; revoke/expiry remain effective. |
| 15 | Update domain consumers I. Depends 14. | I/MiniAppIntegrationService.cs; I/MiniAppSandboxSessionService.cs; I/MiniAppSandboxConfigurationService.cs; T/MiniAppDomainVerificationTests.cs; T/MiniAppSandboxConfigurationTests.cs | Every draft/Sandbox consumer selects actual hostname; two valid hosts cannot cause SingleOrDefault ambiguity or cross-host proof acceptance. |
| 16 | Update domain consumers II. Depends 14,15. | S/SsoClientReader.cs; I/MiniAppSsoService.cs; T/SsoClientPolicyTests.cs; T/MiniAppSsoPostgresTests.cs; T/MiniAppSandboxPolicyTests.cs | Production proof target resolved independently; old-domain revoke/stale proof/client tested. Search all prior one-proof/app assumptions. |
| 17 | Retain immutable version logo. Depends 2,11. | I/MiniAppService.cs; I/MiniAppLogoStorage.cs; I/MiniAppVersionService.cs; T/MiniAppVersionAssetTests.cs | Replacing draft logo cannot delete pinned old asset; unpinned expired uploads still clean up; fake storage only, no real deletion. |
| 18 | Authoritative publication runtime gate. Depends 5,9,10,16. | I/MiniAppPublicationPolicy.cs; S/SsoClientReader.cs; S/SsoClientPolicy.cs; T/MiniAppPublicationPolicyTests.cs; T/SsoClientPolicyTests.cs | Legacy Published alone denied; exact Approved target, hash, live developer/operational/domain/client required; Sandbox rules preserved. |
| 19 | Production registry/origin/consent target. Depends 18. | S/MiniAppOidcRegistry.cs; S/SsoClientReader.cs; S/SsoConsentService.cs; T/MiniAppProductionSnapshotTests.cs; T/SsoConsentEndpointTests.cs | Draft name/logo/ssoEnabled/callback/scopes/type never alter live Production; projected approved config only, exact origin/scopes/redirect. No library fork. |
| 20 | Owner candidate credential preparation. Depends 11,18,19. | I/MiniAppVersionCredentials.cs; H/Developer/MiniAppVersionApi.cs; A/MiniAppVersionContracts.cs; B/version-proxy.test.cjs; T/MiniAppVersionCredentialTests.cs | Explicit owner action for candidate Confidential, existing encryption/no-store; no active-secret revocation by draft edits, Admin/review/export never reveal secret. |

Checkpoint B: editable draft cannot mutate reviewed/published fields, proofs or assets;
no way around publication gate via old SSO endpoints/status. These changes are
Stage 7 integration work, not grounds to claim Stage 5/6 were already version-aware.
Every affected Stage 5/6 regression is kept; update fixtures for legitimate new guards,
never remove protocol/security assertions to make tests pass.

## Tasks 21–30 — submission và Admin review

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 21 | Transactional review audit. Depends 4,9. | I/MiniAppReviewAuditService.cs; A/MiniAppReviewContracts.cs; T/MiniAppReviewAuditTests.cs | Typed actor IDs, before/after/correlation, append-only + payload-bound RequestId; safe AdminLog mirror only with real social User mapping. |
| 22 | Transactional Developer notifications. Depends 8,21. | I/MiniAppReviewNotificationService.cs; A/MiniAppReviewNotificationContracts.cs; T/MiniAppReviewNotificationTests.cs | One inbox/outbox per event/recipient; rollback none; no send before commit or fake social recipient. |
| 23 | Email outbox worker. Depends 22. | I/MiniAppReviewNotificationWorker.cs; I/MiniAppReviewNotificationDelivery.cs; H/Developer/MiniAppReviewRegistration.cs; T/MiniAppReviewOutboxTests.cs; T/MiniAppReviewOutboxPostgresTests.cs | Optional existing SMTP only, lease/CAS, <=100 batch/5 retries, fail explicit, no duplicate in-app; test transport, zero real mail. |
| 24 | Review/publish readiness adapter. Depends 10,16,18. | I/MiniAppReadinessService.cs; A/MiniAppReviewReadinessContracts.cs; T/MiniAppReviewReadinessTests.cs; T/MiniAppReviewReadinessEndpointTests.cs; H/Developer/MiniAppReviewApi.cs | Fresh Stage 6 proof for submit, source/fingerprint recheck, safe blockers, no executor ->blocked; target validation separate from current draft. |
| 25 | Atomic submit + attestation. Depends 11,21,22,24. | I/MiniAppReviewSubmissionService.cs; H/Developer/MiniAppReviewApi.cs; T/MiniAppReviewSubmissionTests.cs; T/MiniAppReviewSubmissionPostgresTests.cs | Seal actual snapshot/evidence in same transaction, own active draft/source, double submit exact winner, rollback/idempotency/tamper and final freshness. |
| 26 | Owned review detail/history APIs + BFF. Depends 25. | I/MiniAppReviewService.cs; H/Developer/MiniAppReviewApi.cs; B/account-proxy.cjs; B/review-proxy.test.cjs; T/MiniAppReviewEndpointTests.cs | Bounded history/actions/change items, readiness/submit/history transport allowlist, deleted/nonowned IDOR denial, snapshot rather than mutable joins; no secret. |
| 27 | Admin read queue/overview/detail. Depends 9,26. | H/Admin/MiniAppReviewAdminApi.cs; I/MiniAppReviewService.cs; A/MiniAppReviewAdminContracts.cs; T/MiniAppReviewAdminReadTests.cs; T/MiniAppReviewAdminEndpointTests.cs | Search/status/assignee/sort/page validated/indexed, safe contact/evidence/capabilities; Admin bearer only. |
| 28 | Start/assign review. Depends 21,22,27. | I/MiniAppReviewDecisionService.cs; H/Admin/MiniAppReviewAdminApi.cs; T/MiniAppReviewAssignmentTests.cs; T/MiniAppReviewAssignmentPostgresTests.cs | One claimant; explicit reassignment with reason; old assignee cannot decide; replay no duplicate notifications. |
| 29 | Reject / request changes. Depends 28. | I/MiniAppReviewDecisionService.cs; H/Admin/MiniAppReviewAdminApi.cs; T/MiniAppReviewDecisionTests.cs; T/MiniAppReviewDecisionPostgresTests.cs | Assignee only, required reason/field items, terminal CAS, two Admin decisions never both commit, immutable history. |
| 30 | Approve exact sealed target. Depends 24,28,29. | I/MiniAppReviewDecisionService.cs; I/MiniAppVersionSnapshotPolicy.cs; T/MiniAppReviewApprovalTests.cs; T/MiniAppReviewApprovalPostgresTests.cs | Recheck target/live safety after I/O, draft edits do not substitute old version, expired domain/locked developer/stale client/suspension denied, no auto publish. |

Checkpoint C: actual HTTP Developer -> review -> actual authenticated Admin decision
works; all events/rollback/retry verified. Sealed evidence remains audit evidence,
not a fresh browser observation forever. Review cannot get authority by editing JSON.

## Tasks 31–36 — publishing, moderation và inbox APIs

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 31 | Exact-version publication readiness + BFF contract. Depends 18,20,30. | I/MiniAppReadinessService.cs; H/Developer/MiniAppPublishingApi.cs; B/account-proxy.cjs; T/MiniAppPublishingReadinessTests.cs; B/publishing-proxy.test.cjs | Explicit approved version, desired Production not Sandbox client, current domain/credential/operational state, no circular pre-publish token requirement. BFF catalog includes publish/unpublish transport; until Tasks 32–33 backend returns 404 for unmapped commands. |
| 32 | Atomic publish/apply registry. Depends 19,21,22,31. | I/MiniAppPublishingService.cs; H/Developer/MiniAppPublishingApi.cs; S/MiniAppOidcRegistry.cs; T/MiniAppPublishingTests.cs; T/MiniAppPublishingPostgresTests.cs | Owner, exact target, one active publication; old publication closes atomically; projection matches approved config, concurrent revisions/retry safe, notify/audit. |
| 33 | Owner unpublish + scoped revocation. Depends 32. | I/MiniAppPublishingService.cs; S/MiniAppPublicationRevocation.cs; H/Developer/MiniAppPublishingApi.cs; T/MiniAppUnpublishingTests.cs; T/MiniAppUnpublishingPostgresTests.cs | Stop Production authority/tokens safely; app-specific only, no social/Admin/other-app logout; idempotency/rollback. |
| 34 | Admin suspend. Depends 28,32,33. | I/MiniAppModerationService.cs; H/Admin/MiniAppModerationApi.cs; S/MiniAppPublicationRevocation.cs; T/MiniAppModerationTests.cs; T/MiniAppModerationPostgresTests.cs | Live Admin permission + reason, atomic operational/publication/audit/outbox; deny new/old protocol use and Sandbox runtime; races fail closed. |
| 35 | Controlled restore. Depends 34. | I/MiniAppModerationService.cs; H/Admin/MiniAppModerationApi.cs; T/MiniAppRestoreTests.cs; T/MiniAppRestoreEndpointTests.cs | Developer cannot restore; live safety checks, stays unpublished, no resurrected token; later explicit republish validates approved target. |
| 36 | Owned inbox/read + BFF. Depends 22,26. | H/Developer/MiniAppReviewNotificationApi.cs; I/MiniAppReviewNotificationService.cs; B/account-proxy.cjs; B/review-notification-proxy.test.cjs; T/MiniAppReviewNotificationEndpointTests.cs | Recipient derived from auth, bounded lists/unread/read idempotency, safe correct app deep link, no cross-owner read. |

Checkpoint D: A/B/C/D backend flows, exact-version Production SSO and suspension
guards work before either Portal exposes publish/decision actions. No public catalog.

## Tasks 37–47 — Developer và Admin Portal

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 37 | Typed FE review/version clients. Depends 12,26,31,36. | F/lib/mini-app-review.ts; F/lib/mini-app-publishing.ts; F/lib/review-notifications.ts; F/tests/mini-app-review.test.ts; F/tests/mini-app-publishing.test.ts | Bounded strict response/input validation, no actor/status/evidence write; safe messages and stale-response guards. |
| 38 | Review tab version/readiness. Depends 37. | F/components/mini-app-review.tsx; F/components/mini-app-detail.tsx; F/components/mini-app-review.css; F/tests/mini-app-review-ui.test.ts; B/review-ui.test.cjs | Rename placeholder; draft/submitted/live separated, server checklist + links, loading/empty/error, no client-computed submit authority. |
| 39 | Submit/history/changes UX. Depends 25,29,30,38. | F/components/mini-app-review.tsx; F/components/review-history.tsx; F/lib/mini-app-review.ts; F/tests/review-workflow.test.ts; B/review-ui.test.cjs | Confirm exact version, pending disables duplicate, escaped reasons/items, new draft/resubmit on changes/reject, meaningful retry/conflict. |
| 40 | Production intent / publish UI. Depends 20,32,33,39. | F/components/mini-app-publishing.tsx; F/components/mini-app-sso.tsx; F/components/mini-app-review.tsx; F/lib/mini-app-sso.ts; F/tests/mini-app-publishing-ui.test.ts | Existing Production editor uses draft API; live config read-only separate; approved-only target confirmation/unpublish, secret prep private; Sandbox flow preserved. |
| 41 | Developer inbox/deep link. Depends 36,39. | F/components/review-notifications.tsx; F/components/mini-app-detail.tsx; F/tests/review-notifications.test.ts; F/developer-screen.web.tsx | Actual Developer inbox/read status, correct owned tab/review deep link, safe navigation/unsaved guards; no social notification rewrite. |
| 42 | Admin module contracts/client/hooks. Depends 27,28,30,34,35. | AD/types/mini-app-review.ts; AD/services/admin-mini-app.service.ts; AD/hooks/useMiniAppReviews.ts; AD/services/admin-mini-app.service.test.mjs; AD/services/admin-mini-app-contract.test.mjs | Existing Axios auth/refresh, allowlisted actions/pagination, typed errors, no storage of review credentials; permission denied never success toast. |
| 43 | Admin overview/queue/navigation. Depends 42. | AD/pages/MiniAppReviewsPage.tsx; AD/components/MiniAppReview/ReviewQueue.tsx; AD/layouts/AdminLayout.tsx; AD/App.tsx; AD/components/MiniAppReview/review-queue.test.mjs | Counts/search/status/sort/paging/logo/developer, one existing module, routes/filter views for history/approved/published/suspended, loading/empty/error. |
| 44 | Admin editorial detail. Depends 43. | AD/pages/MiniAppReviewDetailPage.tsx; AD/components/MiniAppReview/ReviewSnapshot.tsx; AD/components/MiniAppReview/ReviewTimeline.tsx; AD/components/MiniAppReview/mini-app-review.css; AD/components/MiniAppReview/review-detail.test.mjs | Grouped app/developer/domain/legal/config/evidence/timeline, immutable version clearly labeled, no raw JSON/database dump or Client Secret. |
| 45 | Admin decision/assign modals. Depends 28,29,30,44. | AD/components/MiniAppReview/ReviewActions.tsx; AD/components/MiniAppReview/ReviewDecisionDialog.tsx; AD/pages/MiniAppReviewDetailPage.tsx; AD/components/MiniAppReview/review-actions.test.mjs; AD/hooks/useMiniAppReviews.ts | Confirmation + mandatory reasons/items, assignment, server capabilities, busy/conflict/focus and live refresh; cannot forge decision role. |
| 46 | Admin app/publication/moderation views. Depends 34,35,45. | AD/pages/MiniAppsPage.tsx; AD/pages/MiniAppDetailPage.tsx; AD/components/MiniAppReview/ModerationDialog.tsx; AD/App.tsx; AD/components/MiniAppReview/moderation.test.mjs | Published/suspended history and detail, reasoned suspend/restore, explicit unpublished-after-restore, correct server state/capabilities. |
| 47 | Browser accessibility/responsive polish. Depends 40,41,46. | Focused Portal components/styles/tests implicated by observed issues; split if >5 | Actual Developer/Admin browser keyboard/modals/announcements/focus/320/375/768/desktop; no page overflow, malicious reason XSS stays text, bounded diagnostics. |

Checkpoint E: real Admin + Developer UIs call real isolated APIs; no demo decisions,
fake publish result or fake notification receipt. Existing placeholder removal only
in feature scope, no redesign unrelated finance/social/settings screens.

## Tasks 48–53 — final security, E2E và handoff

| # | Task / dependency | Authored files dự kiến (<=5) | Acceptance + verification |
| --- | --- | --- | --- |
| 48 | Cross-cutting security/PG regression. Depends 35,36. | T/MiniAppReviewSecurityTests.cs; T/MiniAppPublishingSecurityTests.cs; T/MiniAppReviewTransactionTests.cs; T/MiniAppReviewSecurityPostgresTests.cs; security mapping doc | All 22 required backend cases + CSRF/IDOR/state/snapshot/version bypass, stale domain/client/source, assignment race, no-secret and forced rollback mapped to executed assertions. |
| 49 | Real isolated review E2E host. Depends 47,48. | tools/SsoE2EHost/Program.cs; tools/SsoE2EHost/ReviewFixture.cs; tools/MiniAppReviewE2E/run-fixture.mjs; tools/MiniAppReviewE2E/portals.fixture.mjs; tools/MiniAppReviewE2E/fixture.test.mjs | Disposable DB/keys/users/domain/test mail; actual account login + Developer BFF auth + module APIs, fresh profiles, bounded runner nonzero on failure/signal, cleanup. No production settings read. |
| 50 | E2E A approved -> actual Production SSO. Depends 49. | tools/MiniAppReviewE2E/approved.e2e.mjs; tools/MiniAppReviewE2E/partner.fixture.mjs; tools/MiniAppReviewE2E/browser-helpers.mjs; tests only if needed | Real Stage 6 proof -> submit -> Admin start/approve -> inbox -> owner publish exact version -> actual Production OIDC; Public/Confidential variants and Sandbox resource isolation. No seeded Approved/Published substitutes. |
| 51 | E2E B/C changes/rejected. Depends 50. | tools/MiniAppReviewE2E/decisions.e2e.mjs; tools/MiniAppReviewE2E/run-fixture.mjs; bounded helpers/tests | Real reasons/change items/inbox/new version/new checks/resubmit/approve; rejected version publish denied; duplicate/concurrent mutation produces conflict, never contradictory success. |
| 52 | E2E D suspension + draft isolation. Depends 51. | tools/MiniAppReviewE2E/moderation.e2e.mjs; tools/MiniAppReviewE2E/isolation.e2e.mjs; bounded fixture/helper/test updates | Suspend blocks authorize/code/token/UserInfo/introspection; restore unpublished; republish/new consent; mutate draft while old reviewed/published and prove exact old target retained; stale proof/client fail closed. |
| 53 | Final review/regression/report. Depends 52. | docs/handoffs/developer-platform-stage-7.md; docs/operations/developer-platform-stage-7.md; docs/testing/developer-platform-stage-7-security.md; spec/plan status | All relevant builds/types/lint/suites/PG/migrations/4 browser flows recorded honestly; 17 report categories, remaining issues, rollout + Stage 8 prerequisites, no Stage 8 start. |

## Verification commands và checkpoint policy

Commands đầy đủ ở [spec mục 11](../specs/developer-platform-stage-7.md#11-commands-và-testing).
Mỗi task chạy focused tests tương ứng tên class/file trong bảng và build affected
project. Sau checkpoint B/D/F chạy broader SSO/Sandbox/Developer/Admin regressions.
Không lặp full suite khi source không đổi hoặc chỉ để tạo thêm log pass.
PG tests luôn dùng disposable cluster, không installed service/application DB.
Test nào fail/skip phải ghi root cause/ảnh hưởng, không đổi sang giả lập success.

Migration script review, offline model check, known SDK .NET8 Release, FE tsc/Expo
export, SDK/host Node và Admin build/lint là release evidence. Audit dependency
versions từ installed manifests; không upgrade framework/package trong feature này.
Nếu Admin chưa có runner, dùng existing Node native tooling + installed TS tooling
trong test fixture, xác minh support trước; dependency mới phải pin và human review.

Checkpoint F (sau Task 53): all acceptance fulfilled, no high-severity open finding,
all 4 E2E actual flows, 22 backend requirements và UI cases traceable. Explicit limits:
seeded ownership/test identities, local CA/DNS mapping, no deployed-header/public
domain/native assurance, no real mail, no live Production DB/app publication.

## Rủi ro và xử lý

| Rủi ro | Giảm thiểu / gate |
| --- | --- |
| Production còn đọc current draft | Tasks 13,18,19 trước publish API; old direct SSO write blocked; exact approved target enforced everywhere. |
| Draft đổi domain/logo phá version cũ | Tasks 14–17 update index **và mọi consumer**, preserve target proof/assets, explicit revoke vẫn deny. |
| Stage 6 source/5 phút/retention không phù hợp queue review | Human duyệt sealed submission attestation; independently revalidate target live safety, không giả mới proof cũ. |
| Mixed identity notification/audit | Developer inbox FK riêng, typed actor FK; không gán DeveloperAccountId/AdminAccountId thành social UserId. |
| Two Admin / publish-suspend race | Serializable + CAS + unique indexes + idempotency, scoped permission recheck; PG real races. |
| Active secret bị đổi vì draft khác type | Candidate preparation không revoke active credential; only publish/explicit rotation changes runtime; no secrets in snapshot/review. |
| Legacy Published bypass/new schema deployment | No auto approvals; gate actual publication, operator rollout/migration separate, backward downgrade safety tested. |
| Side effects trước commit / retry spam | Inbox/outbox/action atomic, worker leases/dedup/bounded retry, SMTP ambiguity documented. |
| “E2E” chỉ mock/token tự mint | Account login/BFF/module/Stage 6 executor/Production OIDC thật trong fixture; unit seeding không dùng làm proof nghiệp vụ. |

## Definition of done / handoff

Final report gồm đúng 17 nhóm: architecture, schema, migration, API, Developer UI,
Admin UI, state machine, versioning, notifications, audit, security, unit tests,
integration, E2E, PostgreSQL, remaining issues, Stage 8 prerequisites.
Checkpoint ghi tasks/files/RED-GREEN commands/results/risks và bảo toàn unrelated edits.
Không triển khai sản phẩm trước human approval của hai tài liệu này; không tự thực
hiện deployment, production migration hay Stage 8 sau khi hoàn thành.
