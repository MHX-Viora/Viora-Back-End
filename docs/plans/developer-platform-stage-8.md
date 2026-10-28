# ANKT Developer Platform — Stage 8 implementation plan

## 2026-10-23 native continuation audit

Continue the existing dedicated-shell design; native runtime/SSO acceptance stays
gated. Preserve existing dirty work and Web acceptance.

- [x] Repair native terminal notification after shell listener disposal. Verify
  existing failing ProductionHostTests/native.test.mjs plus host regressions.
- [x] Reproduce adapter native-error teardown and late-callback behavior; unify
  terminal cleanup and verify native adapter/channel/navigation tests and types.
- [x] Verify Android module/debug assembly with the installed toolchain. Record
  actual emulator availability separately from native business/device evidence.
- [x] Write native handoff with reproducible checks and unresolved P0/iOS gates.

Verification: 50 frontend checks, 16 host checks, root types/focused lint and
Android debug assembly pass. Backend quota regression revealed a SQLite clock
precision defect in its fixture; parameterized timestamp passes all five quota
cases. Final Production regression: 146 passed, zero failures/skips; details in
the native handoff. Native business/security/SSO acceptance remains gated.

Status: **IMPLEMENTING — APPROVED**, 2026-10-22, theo yêu cầu người dùng
“chạy làm giai đoạn 8”. [Specification](../specs/developer-platform-stage-8.md).
Progress, executed verification và limitations tại
[handoff](../handoffs/developer-platform-stage-8.md). Chưa hoàn tất Stage 8 cross-platform.

2026-10-23 scope decision: user chose **Web first; native not accepted**.
Web slices 38a–c and 43–52 now have real embedded Public/Confidential SSO,
non-SSO Utilities/Wallet, live moderation, hostile Web checks, source-lock races,
redaction and measured observations. See [Web checkpoint](../handoffs/developer-platform-stage-8-acceptance.md)
for the evidence matrix and fixture limits. Native portions of 33–42/44/46–51
remain gated; no device proof or cross-platform completion is implied.

2026-10-23 checkpoint: configurable quota follow-up complete, UI browser harness
and real Public/Confidential publication-to-Web-host pipeline verified locally.
Backend regression 832 passed / 1 existing skip; see handoff for exact scope,
cleanup evidence; the later Web completion checkpoint supersedes its former
embedded-SSO/business/security gaps. Native gates remain outstanding.

## Execution và verification conventions

Làm theo dependency, từng vertical slice: đọc source/spec -> failing behavior test
-> minimum change -> GREEN -> affected build/types/lint -> diff/security review ->
handoff checkpoint. Tối đa khoảng 5 authored files/task; nếu cần nhiều hơn, tách
trước code. EF Designer/snapshot/native generated outputs ghi riêng, không dùng
để che broad authored rewrite. Không commit/deploy/real DB/email/Stage 9.

Abbreviations: D=`Viora.Domain/Developers`, A=`Viora.Application/Developers`,
I=`Viora.Infrastructure/Developers`, P=`Viora.Infrastructure/Persistence`,
H=`viora-BE/Production`, S=`Viora.Infrastructure/Sso`, T=`Viora.Application.Tests`,
F=`../viora/features/mini-apps`, C=`../viora/components/mini-apps`,
K=`../viora/packages/miniapp-sdk`, X=`tools/MiniAppProductionE2E`.
Tên file chưa tồn tại là output dự kiến; chỉnh tên theo convention source khi code.

Verification aliases, chạy từ backend root trừ khi ghi rõ:
- `B(Name)`: .NET8 local `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -p:UseSharedCompilation=false --filter "FullyQualifiedName~Name"`.
- `BUILD`: .NET8 local `dotnet build viora-BE.sln --no-restore -m:1 -p:UseSharedCompilation=false`.
- `FE(Name)`: từ ../viora `node --import tsx --test --experimental-test-isolation=none features/mini-apps/tests/Name.test.ts` và `npx tsc --noEmit`.
- `SDK`: từ ../viora `npm --prefix packages/miniapp-sdk test`.
- `HOST(Name)`: `node --test tools/ProductionHostTests/Name.test.mjs`.
- `PG(Name)`: `B(Name)` với IsolatedPostgres owned cluster; không application connection.
- Commands full regression/build/native/browser tại spec mục 11. No rerun suite khi
  source không đổi và không có failure/new concern; fixture không được mock success.

## Phase 0 — approval và feasibility trước native commitments

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 1 | Toolchain/dependency gate + native boundary proof. Gate: spec approval. | ../viora/package.json; lockfile; X/native-feasibility.mjs; X/native-partner.fixture.mjs; F/tests/native-boundary.test.ts | Record actual devices/toolchain/iOS availability; advisory + Expo54 compatibility review before installing approved WebView pin. Build isolated native probe and prove wrapper/child isolation, file/media denial even with ANKT OS permissions granted. Nếu library không đủ, specify minimal native adapter before product code. Verify FE(native-boundary) + actual owned Android fixture; no iOS pass inferred. |
| 2 | Native SSO feasibility Public/Confidential. Depends 1. | X/native-sso.fixture.mjs; X/native-sso.test.mjs; X/native-partner.fixture.mjs; docs/decisions/stage-8-native-sso.md | Real issuer/system browser/partner exchange; source user vs browser user, state/nonce/PKCE, reload/reopen, cookie isolation/cancel/replay demonstrated. No browser-cookie copy, mock sessions or secret relay. If a new broker/trust change needed, exact contract/ADR submitted for human review before that implementation. |
| 3 | Resolve native contract/security gate P0. Depends 2. | spec; plan; docs/decisions/stage-8-native-sso.md; docs/handoffs/developer-platform-stage-8.md | Freeze supported handoff, schemas/auth/lifetimes, source/return provenance, platform limitations and native plugin scope. Unsupported cases remain disabled; no blanket Stage 8 complete. Verify requirements/spec/source traceability; human decision recorded for changed trust boundary. |

P0: local Stage 5–7 baseline accepted; native security/SSO gaps explicit. Web
foundation may proceed independently after initial approval; **native auth product
tasks depend on resolved P0**, not merely a scaffold adapter. No silent fallback.

## Phase 1 — shared eligibility và catalog

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 4 | Production public contracts/policy options. Gate: approval. | A/MiniAppProductionContracts.cs; I/MiniAppProductionOptions.cs; T/MiniAppProductionContractTests.cs | Bounded immutable DTOs/IDs/error schema; default disabled and Stage 7 dependency; no actor/URL/capability override. B(MiniAppProductionContract) + BUILD. |
| 5 | Common publication eligibility. Depends 4. | I/MiniAppProductionEligibility.cs; I/MiniAppPublicationPolicy.cs; T/MiniAppProductionEligibilityTests.cs; T/MiniAppPublicationPolicyTests.cs | Exact sealed Approved/Published target, live domain/developer/state, non-SSO without client supported; SSO legacy bypass remains denied. B(MiniAppProductionEligibility), B(MiniAppPublicationPolicy), existing SsoClientPolicy tests + BUILD. |
| 6 | Catalog projection entity/mapping. Depends 5. | D/MiniAppCatalogPublication.cs; P/Configurations/MiniAppCatalogPublicationConfiguration.cs; P/AppDbContext.cs; T/MiniAppProductionCatalogPersistenceTests.cs | Immutable display/hash, 1:1 publication and same-app version/review FKs, no independent status. B(MiniAppProductionCatalogPersistence) + BUILD. |
| 7 | Catalog migration + DB invariants. Depends 6. | additive migration authored SQL; T/MiniAppProductionCatalogMigrationTests.cs (+ generated) | Disposable PG Up/Down/Up, malformed/cross-app rows/updates denied, preserve Stage 5–7. PG(MiniAppProductionCatalogMigration) + offline model comparison. |
| 8 | Transactional publish projection. Depends 7. | I/MiniAppPublishingService.cs; I/MiniAppCatalogProjection.cs; T/MiniAppProductionCatalogProjectionTests.cs; T/MiniAppPublishingPostgresTests.cs | Publish and projection one transaction; rollback/replay no duplicate; unpublish/suspend hidden by live authority. B(MiniAppProductionCatalogProjection), PG(MiniAppPublishingPostgres) + BUILD. |
| 9 | Explicit reconcile/backfill tooling. Depends 8. | tools/MiniAppCatalogReconcile/Program.cs; tool csproj; I/MiniAppCatalogProjection.cs; T/MiniAppProductionCatalogBackfillTests.cs | Default dry-run, validate existing actual publication/hash, no approval/publish fabrication; changed state causes skip/conflict. B(MiniAppProductionCatalogBackfill); exercise disposable DB only. No application execution. |
| 10 | Query eligibility before pagination. Depends 8. | I/MiniAppPublicCatalogService.cs; A/MiniAppProductionContracts.cs; T/MiniAppProductionCatalogTests.cs; T/MiniAppProductionCatalogPostgresTests.cs | Invalid app/developer/proof/client excluded before Count/Order/Page; stable tie order/page bounds, public-only fields; no network N+1. B(MiniAppProductionCatalog), PG(MiniAppProductionCatalogPostgres) + BUILD. |
| 11 | Public list/detail API and registration. Depends 10. | H/MiniAppPublicApi.cs; H/MiniAppProductionRegistration.cs; H/MiniAppProductionFilter.cs; viora-BE/Program.cs; T/MiniAppProductionCatalogEndpointTests.cs | Anonymous GET paths, unavailable uniform404, strict paging/no-store/safe errors, scoped module policy only. B(MiniAppProductionCatalogEndpoint) + BUILD. |

Checkpoint A: catalog derives only real Stage 7 approved publications; non-SSO
works; exact list totals/filters and existing SSO regressions pass. No host launch yet.

## Phase 2 — launch session, grants và trusted host

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 12 | Host session/grant contracts/entities. Depends 5. | D/MiniAppProductionHostEntities.cs; A/MiniAppProductionHostContracts.cs; P/Configurations/MiniAppProductionHostConfigurations.cs; P/AppDbContext.cs; T/MiniAppProductionHostPersistenceTests.cs | Nullable actor/source binding, exact publication/version/hash, hashed grants, bounded TTL/request replay constraints. B(MiniAppProductionHostPersistence) + BUILD. |
| 13 | Host session migration. Depends 12. | additive migration; T/MiniAppProductionHostMigrationTests.cs (+ generated) | PG Up/Down/Up, no cross-app/session/grant substitution, concurrent receipts unique, no change to Developer/Sandbox/OIDC rows. PG(MiniAppProductionHostMigration). |
| 14 | Anonymous/authenticated source policy. Depends 11,13. | I/MiniAppProductionAccess.cs; H/MiniAppProductionAuthentication.cs; T/MiniAppProductionAccessTests.cs; T/MiniAppProductionAuthEndpointTests.cs | Public opening independent of ANKT login; provided invalid/expired bearer denied; authenticated source requires live signed sid/account. Origin/CSRF binding separate from social mutation paths. B(MiniAppProductionAccess), B(MiniAppProductionAuthEndpoint) + BUILD. |
| 15 | Runtime audit mapping + retention schema. Depends 13. | D/MiniAppProductionRuntimeEvent.cs; P/Configurations/MiniAppProductionRuntimeEventConfiguration.cs; P/AppDbContext.cs; T/MiniAppProductionEventPersistenceTests.cs | Fixed event fields/codes/length limits, no private payload storage; typed nullable actor refs. B(MiniAppProductionEventPersistence) + BUILD. |
| 16 | Audit migration + transactional writer. Depends 15. | additive migration; I/MiniAppProductionAudit.cs; T/MiniAppProductionAuditTests.cs; T/MiniAppProductionAuditMigrationTests.cs (+ generated) | Redaction, rollback/replay, PG constraints and downgrade preserve existing publication/SSO. B(MiniAppProductionAudit), PG(MiniAppProductionAuditMigration). |
| 17 | Host prepare/browser binding. Depends 14,16. | I/MiniAppProductionBindingService.cs; H/MiniAppProductionHostApi.cs; T/MiniAppProductionBindingTests.cs; T/MiniAppProductionHostEndpointTests.cs | Empty HTTPS/exact-host shell, entropy/hash/nonce/expiry, no public app leak; limits/CSRF. B(MiniAppProductionBinding), B(MiniAppProductionHostEndpoint) + BUILD. |
| 18 | Launch preflight and command. Depends 17. | I/MiniAppProductionLaunchService.cs; H/MiniAppLaunchApi.cs; H/MiniAppProductionRegistration.cs; T/MiniAppProductionLaunchTests.cs; T/MiniAppProductionLaunchPostgresTests.cs | URL from sealed target only, public network check outside transaction, final eligibility/actor revisions checked, one payload-bound receipt. B(MiniAppProductionLaunch), PG(MiniAppProductionLaunchPostgres) + BUILD. |
| 19 | One-use redeem/runtime cookie. Depends 18. | I/MiniAppProductionGrantService.cs; H/MiniAppProductionHostApi.cs; T/MiniAppProductionGrantTests.cs; T/MiniAppProductionGrantPostgresTests.cs | Wrong host/binding/nonce/audience/replayed grant denied; PG one winner, live recheck at commit; scoped HttpOnly cookie, no URL credentials. B(MiniAppProductionGrant), PG(MiniAppProductionGrantPostgres). |
| 20 | Heartbeat/close/revalidation. Depends 19. | I/MiniAppProductionRuntimeService.cs; H/MiniAppProductionHostApi.cs; T/MiniAppProductionRuntimeTests.cs; T/MiniAppProductionRuntimePostgresTests.cs | Every context/action/poll checks pinned publication/domain/client/source; changed publication invalidates session, close idempotent scoped only; no social logout. B(MiniAppProductionRuntime), PG(MiniAppProductionRuntimePostgres). |
| 21 | Event ingress and cleanup. Depends 20. | I/MiniAppProductionRetentionService.cs; I/MiniAppProductionWorker.cs; H/MiniAppProductionHostApi.cs; H/MiniAppProductionRegistration.cs; T/MiniAppProductionRetentionTests.cs | Fixed-code quotas, proposed retention, no execution of diagnostics as authority; bounded expired-row/grant cleanup and cancellation. B(MiniAppProductionRetention) + BUILD. |
| 22 | Host shell/assets/CSP. Depends 20. | H/MiniAppProductionHostApi.cs; H/Host/host.mjs; H/Host/host.css; viora-BE/viora-BE.csproj; T/MiniAppProductionHostHeaderTests.cs | Direct partner frame, exact ancestor/frame/connect policies, permission deny, no top-navigation/proxy, shell metadata only after redeem. B(MiniAppProductionHostHeader) + BUILD. |
| 23 | Shared v1 Production bridge. Depends 22. | H/Host/bridge.mjs; tools/ProductionHostTests/bridge.test.mjs; T/MiniAppProductionRuntimeTests.cs | Original v1 envelopes/context/close and strict origin/source/handshake/IDs/quotas/generations; Production from server; unsupported native/auth/v2 not advertised. HOST(bridge), B(MiniAppProductionRuntime), SDK. |
| 24 | Host navigation/external confirmation. Depends 23. | H/Host/navigation.mjs; H/Host/host.mjs; tools/ProductionHostTests/navigation.test.mjs; tools/ProductionHostTests/lifecycle.test.mjs | Parsed exact origins, deny dangerous schemes, explicit external/open/close, abort/reload cleanup, no forged back URL. HOST(navigation), HOST(lifecycle). |

Checkpoint B: PG launch/redeem/publish/suspend races, binding/CSRF/runtime and v1
compatibility pass before app-facing host can launch. Sandbox authority remains separate.

## Phase 3 — Tiện ích và Web host integration

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 25 | Public typed client/cache. Depends 11. | ../viora/services/mini-app.service.ts; F/lib/contracts.ts; F/lib/catalog-cache.ts; F/tests/catalog-client.test.ts | Strict public fields/IDs, abort/stale guard, bounded pages, 30s display cache, anonymous fetch, launch always network; no token to logo/partner. FE(catalog-client). |
| 26 | Responsive Mini App section. Depends 25. | C/mini-app-section.tsx; C/mini-app-grid.tsx; F/lib/grid-layout.ts; F/tests/catalog-ui.test.ts; F/tests/grid-layout.test.ts | Skeleton/empty/error/retry/logo fallback/2-line labels, dynamic columns, accessible press/load-more; no mock/category filter. FE(catalog-ui), FE(grid-layout). |
| 27 | Integrate below Wallet. Depends 26. | ../viora/features/utilities/utilities-screen.tsx; F/tests/utilities-integration.test.ts | Same header/Wallet/actions/banner/theme; independent failures and focus/reconnect refresh, no Wallet fetch regression. FE(utilities-integration); wallet-summary-card native Node test + types. |
| 28 | Host route/state controller. Depends 18,25. | ../viora/app/mini-apps/[appId].tsx; F/mini-app-host-screen.tsx; F/lib/host-controller.ts; F/tests/host-controller.test.ts; ../viora/app/_layout.tsx | Exact host route anonymous exception, protected tabs unchanged, launch guards/double press, safe header/errors/close/back/offline and no arbitrary URL parameter. FE(host-controller) + root auth regression. |
| 29 | Web host adapter/transport. Depends 24,28. | F/adapters/host.web.tsx; F/lib/host-transport.ts; F/tests/host-transport.test.ts; F/tests/web-host.test.ts | Prepare/nonce/source-bound grant POST, dedicated iframe, no JWT/code/secret to partner, all timers disposed. Frame load is not success; explicit unavailable/fallback UX. FE(host-transport), FE(web-host). |
| 30 | Web CSP route wiring. Depends 29. | ../viora/server/mini-app-host-policy.cjs; ../viora/server/developer/vercel-base.json; ../viora/scripts/build-mini-app-deployment.cjs; ../viora/package.json; F/tests/host-route-policy.test.ts | Compose existing deployment header baseline with a separate public-host policy, only for the host route. Do not route public endpoints through Developer BFF; retain Developer/Sandbox/document protection and avoid recreating unrelated deleted deployment config. FE(host-route-policy), npm run test:developer + Web export; inspect generated artifact. |
| 31 | V1 SDK convenience helpers. Depends 23. | K/src/bridge.ts; K/tests/bridge.test.mjs; K/README.md | getEnvironment derives existing v1 getContext, isInside requires handshake; old v1/v2 Sandbox contracts pass, no extra advertised permission old SDK rejects. SDK + root types. |
| 32 | Real Web accessibility/responsive. Depends 27,30,31. | X/web-ui.e2e.mjs; focused F/C implicated styles/components (split if >5) | Chrome 320/375/768/1440, keyboard/focus/modals/scroll, actual overflow/console, offline/retry/XSS text. Real observations drive fixes; fixture-simulated list not final business evidence. FE affected + Chrome screenshots. |

Checkpoint C: actual Utilities/Wallet unchanged, Web host usable through real API;
SDK v1 compatibility and controlled-origin navigation pass. No native claim yet.

## Phase 4 — native containers và SSO

2026-10-23 completion continuation: Task 38 splits into 38a (bounded partner-owned
Confidential transaction/session endpoints plus boundary tests, no host auth
change), 38b (Public SDK popup + Confidential popup/session revalidation inside
the existing v1 partner fixture), 38c (real first/deny/retry/repeat/new-scope and
reload/reopen assertions for both client types). Production callbacks/scopes are
configured through the real draft/review workflow, never seeded as approved.
Then complete independent Web slices of Tasks 46,48–50 and 43–44; native remains
subject to P0/device evidence. No claim that a Web checkpoint accepts all platforms.

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 33 | Product development-build configuration. Depends 1,3. | ../viora/app.json; ../viora/plugins/with-mini-app-host.cjs; F/tests/native-config.test.ts; docs/decisions/stage-8-native-sso.md | Reuse dependency pin already approved/verified in Task 1; no framework upgrade. Configure additive native host policy and build Android with isolated debug config; record iOS build availability. Generated native changes listed separately, no release keys. FE(native-config). |
| 34 | Android container. Depends 28,33. | F/adapters/host.native.tsx; F/lib/native-navigation.ts; F/tests/native-navigation.test.ts; F/tests/native-host.test.ts | Trusted wrapper only, pre-first-load validation, TLS/mixed/file/new-window policies, close/loading/error/progress; no native auth from self-reported origin. FE(native-navigation), FE(native-host) + actual Android. |
| 35 | Android permission/chooser deny adapter. Depends 34. | ../viora/plugins/with-mini-app-host.cjs; <=3 authored native policy files agreed at P0; F/tests/native-permissions.test.ts | Deny camera/mic/location/chooser/download even if global app permissions already granted, no effect on Social/calls features. Test malicious child and unsupported methods; actual Android build/runtime, not JS snapshots only. |
| 36 | Native lifecycle/private shell channel. Depends 34,35. | F/lib/native-host-channel.ts; F/adapters/host.native.tsx; F/tests/native-host-channel.test.ts; F/tests/native-lifecycle.test.ts | Nonce/generation/actual trusted document, subframe spoof denied, hardware back/resume/death/reload/teardown; no grant restored from persistence. FE(native-host-channel), FE(native-lifecycle) + Android device. |
| 37 | iOS container policy/recovery. Depends 33,36. | F/adapters/host.ios.tsx or shared adapter; <=2 approved iOS native policy files; F/tests/ios-policy.test.ts | WK permission/cookie/navigation/keyboard/safe-area/process termination, no blanket cookie-sharing. FE(ios-policy); Mac/Xcode build + simulator/device E2E if available; else explicitly NOT VERIFIED. |
| 38 | Web Production SSO lifecycle. Depends 30,31. | X/production-partner.fixture.mjs; X/web-sso.e2e.mjs; F/tests/sso-lifecycle.test.ts | Public PKCE and Confidential backend exchange from embedded host, first/deny/repeat/new scopes, session reload/reopen actual validation. SDK, FE(sso-lifecycle), real browser/issuer. |
| 39 | Native first-party SSO transport. Depends 3,36,37. | Files explicitly approved in P0 ADR, <=5 per slice; T/MiniAppProductionNativeSsoTests.cs; F/tests/native-sso.test.ts | Signed live source + browser binding, mismatch/login-CSRF/cancel/replay expiry denied; original Social Auth/refresh untouched. No product broker until P0 contract review. B(MiniAppProductionNativeSso), FE(native-sso) + actual native fixture. |
| 40 | Native Public SDK response adapter. Depends 39. | K/src/native-auth.ts; K/src/popup.ts; K/src/index.ts; K/tests/native-auth.test.mjs; X/native-public.e2e.mjs | Opt-in compatible adapter from P0; SDK retains PKCE/state/nonce and validates real callback/code/token; no host-held social token/secret. SDK + actual Android/iOS flow, no inferred cookie sync. |
| 41 | Native Confidential continuation/session. Depends 39. | X/native-confidential.e2e.mjs; K/examples/confidential/native-continuation.mjs; K/tests/native-partner-session.test.mjs; K/README.md | Real backend-only exchange and verified one-use partner-owned session handoff/revalidation, replay/wrong browser denied; host receipt alone cannot authenticate. SDK + actual native. |
| 42 | Native platform UX and E2E checkpoint. Depends 35–41. | X/native-ui.e2e.mjs; implicated adapter/styles <=4 | Device safe-area/keyboard/back, permission denial, upload/download unsupported, process kill/resume/offline/close, memory/listener cleanup; record device/build/OS. No complete native claim without evidence. |

Checkpoint D: Web/Android/iOS implementation/build/E2E columns separately recorded;
P0 resolved before native SSO product code. iOS absence yields explicit verification
gap; external browser fallback is not equivalent host/bridge success.

## Phase 5 — security, business E2E và final report

Resume slices (2026-10-23): Task 32 UI-only harness precedes real API evidence.
Task 45 splits into 45a (Production v1 partner runtime, preserving Sandbox v2),
45b (loopback HTTPS portal using actual catalog/host components and launch client),
45c (invoke after real Stage 6 checks/review/approval/publication; assert anonymous
catalog -> binding -> launch -> redeem -> v1 context/close and fresh reopen).
Each slice remains <=5 authored implementation files. This pipeline alone is
neither non-SSO Flow A nor Production SSO Flow B acceptance; those need their own
client/publication/account evidence. Native gates stay unchanged.

Task 21 quota follow-up splits into 21a (bounded configurable HTTP module policies
and real TestHost limit/isolation tests) and 21b (use the same validated launch
limit for binding/account history and a configurable per-session event limit).
Defaults/ceilings remain prepare 30, launch 20, redeem 30, runtime 120 and events
10 per minute; operators may lower these values. No global/auth limiter changes.

| # | Task / depends | Authored files dự kiến (<=5) | Acceptance và verification |
| --- | --- | --- | --- |
| 43 | Publication/launch race matrix. Depends 20,29. | T/MiniAppProductionSecurityTests.cs; T/MiniAppProductionRacePostgresTests.cs; T/MiniAppProductionTransactionTests.cs; docs/testing/developer-platform-stage-8-security.md | All 16 backend cases mapped; 2 clients/users, anonymous/auth binding, lost reply, suspend/publish/redeem race, rollback/audit redaction, no stale URL fallback. B(MiniAppProductionSecurity), PG(MiniAppProductionRacePostgres), B(MiniAppProductionTransaction). |
| 44 | Bridge/navigation/permission hostile matrix. Depends 24,36,37,40,41. | tools/ProductionHostTests/security.test.mjs; F/tests/host-security.test.ts; K/tests/bridge.test.mjs; security mapping | Forged source/origin/handshake/nonces, scheme/redirect/TLS, native already-granted permissions, Sandbox token/resource/Production client isolation. HOST(security), FE(host-security), SDK and native security fixture. |
| 45 | Real isolated business E2E fixture. Depends 32,38,43. | tools/SsoE2EHost/ProductionFixture.cs; tools/SsoE2EHost/Program.cs; X/run-fixture.mjs; X/fixture.test.mjs; X/portals.fixture.mjs | Actual user/Admin/Developer login, Stage 6 executor, real submit/approve/publish, public catalog/host; generated keys/disposable PG/mail-discard; bounded runner/owned cleanup. No seeded approvals/publications substitute. Native fixture wiring separate small slice if >5 files. |
| 46 | Flow A non-SSO. Depends 45. | X/non-sso.e2e.mjs; X/production-partner.fixture.mjs | Review/publish -> Utilities below Wallet -> open/use/close/return, anonymous deep link, published non-SSO no OAuth client; no fake rows. Run actual Web + native where verified. |
| 47 | Flow B SSO Public/Confidential. Depends 40,41,45. | X/sso.e2e.mjs; X/native-sso.e2e.mjs; X/production-partner.fixture.mjs | Real ANKT login/bootstrap/consent/code/token/ID-token/UserInfo/partner session, reload/reopen, fast path and deny/new scopes. Report each platform/client type; no mocked authorization result. |
| 48 | Flow C suspension/recovery. Depends 46,47. | X/moderation.e2e.mjs | Actual Admin suspend hides catalog/blocks new launch/halts active bridge/revokes scoped tokens; old code denied. Restore unpublished, explicit republish, old grants remain invalid; unrelated social sessions survive. |
| 49 | Flow D security and app isolation. Depends 48. | X/security.e2e.mjs; X/negative-partners.fixture.mjs | Actual redirect mismatch/cross-source/forged Bridge/unpublished/Sandbox token/dangerous scheme/secret leak/invalid TLS/unauthorized permission negatives. Every failure asserted; exit0 does not mean negative runtime succeeded. |
| 50 | Performance/lifecycle observations. Depends 49. | X/performance.e2e.mjs; docs/testing/developer-platform-stage-8-performance.md | Measure real catalog queries/latency/pages/logo, launch and repeated close/reopen memory/listeners/SSO attempts; Wallet independent, no arbitrary claimed targets. Optimize only measured feature bottlenecks and reverify affected cases. |
| 51 | Final regression + review. Depends 42–50. | security map; docs/handoffs/developer-platform-stage-8.md | Run relevant backend/PG/SSO/Sandbox/Developer/SDK/host/Wallet/root-auth suites + Release/model/types/lint/export and native builds; record all skips/failures/warnings, no unverified platform pass. Review correctness/security/architecture/performance and close high severity findings. |
| 52 | Operations/acceptance/status report. Depends 51. | docs/operations/developer-platform-stage-8.md; docs/handoffs/developer-platform-stage-8-acceptance.md; handoff; spec; plan | Final 21 groups from user XXVI, 20 FE/16 BE cases/4 flows/platform matrix traceable; rollout/rollback/backfill/privacy/cookie limits. Mark complete only within proven scope, no Stage 9/deploy/production migration. |

Checkpoint E (final): no high-severity open finding; actual required business flows,
security negatives and native platform evidence recorded honestly. Full Stage 8
cross-platform acceptance cannot be marked complete while native SSO P0 is unresolved
or required device execution is absent. A Web-only milestone must be named as such.

## Rollback và risks

| Risk | Mitigation / stop condition |
| --- | --- |
| Catalog accidentally uses mutable draft/legacy status | Immutable publication projection + live shared SQL eligibility, exact hash/target and regression. Missing projection hidden; dry-run reconciliation first. |
| Query filtering after pagination | PostgreSQL tests for total/page with invalid rows and expired proofs; no per-row external HTTP. |
| Anonymous route broadens Social access | Exact host route exception only; root-auth/Wallet regression and bearer/source checks. |
| Native RN message origin spoof | Trusted wrapper iframe v1 protocol; private channel nonce/generation and actual top document; P0 hostile-frame proof before enablement. |
| OS permission already granted leaks to partner | Explicit native permission/chooser policy, not a JS prop assumption; test on an already-permitted app. |
| Cookie isolation breaks mobile SSO | Real Public/Confidential feasibility and partner-owned handoff, no cookie injection, explicit blocked requirement if unproved. |
| SDK change breaks v1/v2 | Preserve v1 handshake/context/close and Sandbox v2 tests; no unnegotiated permission additions. |
| iOS verification impossible on Windows | Preserve build/test evidence separately; Mac/device execution is an outstanding gate, no full-support claim. |
| Schema downgrade loses history or reopens old paths | Stage 8 disable/revoke sessions before downgrade; retain Stage 7 gate, backup and operator approval; PG rollback verified. |

Work sequentially; UI pure grid/client work can be independent of backend migration
after contracts, but shared policy/auth/migrations/native handoff require ordered
checkpoints. Không tự tạo fan-out agents hoặc sửa chức năng ngoài Stage 8.

## Human review requested

Duyệt assumptions/options ở spec mục 3 và 12, đặc biệt anonymous route, dedicated
origin/wrapper, v1 permission baseline, WebView pin/native permission deny, native
SSO P0 và additive projection/launch schema. Gate được yêu cầu trực tiếp tại mục
XXIV: **“Trình human review trước khi code. Không tự động triển khai khi chưa được
phê duyệt.”** Người dùng đã cho phép implementation ngày 2026-10-22; chạy Task 1/4
theo dependency. Không có approval ngầm
cho broker/trust model chưa được mô tả hoặc cho deployment.
