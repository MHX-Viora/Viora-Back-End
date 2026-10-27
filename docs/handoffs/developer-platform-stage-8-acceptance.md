# Stage 8 — Web completion checkpoint

2026-10-23. User scope decision: **“Chốt Web trước; ghi native chưa nghiệm thu.”**
This report closes the isolated Web milestone. Android/iOS and full cross-platform
Stage 8 acceptance remain **NOT VERIFIED**. Runtime stays disabled by default;
this is not deployment approval. Latest final regression totals are recorded in
the [handoff](developer-platform-stage-8.md).

## Actual business flows

| Flow | Web evidence | Limits |
| --- | --- | --- |
| A, non-SSO | `non-sso.e2e.mjs`: actual Stage 6 checks, submission/approval/publish; no Production OAuth client; anonymous deep link; actual Social password login, Utilities below Wallet, authenticated host, close/return, one Wallet focus refresh; expired domain hides catalog while Wallet works | Real WalletService and persisted wallet; unrelated authenticated background endpoints are outside the isolated fixture DI graph and emit missing-service errors. This does not certify the entire Social application. |
| B, Public | `web-sso.e2e.mjs` + actual SDK: deny/retry/allow, repeat fast path, added profile scope consent, repeat; real code/token/ID token/UserInfo; reload and fresh host reopen | Public tokens are memory-only: reload/reopen requires explicit login again. Chrome fixture cookie behavior is not universal browser compatibility. |
| B, Confidential | Same real issuer/consent sequence; partner backend exchange with PKCE/state/nonce; HttpOnly partner cookie; reload/reopen revalidates fresh UserInfo | Completion message alone never authenticates. No secret or token is handed to parent host. |
| C, Public + Confidential | `moderation.e2e.mjs`: actual Admin suspend; catalog/detail/new launch denied; active heartbeat removes iframe/ends session; old UserInfo/introspection/held code denied; restore stays unpublished; explicit republish opens fresh host | Social session state preserved; no claim that revoked tokens become valid after recovery. |
| D, Web | `security.e2e.mjs`: forged host/private-channel messages cannot close session; SDK context still valid; dangerous scheme denied; camera/microphone/geolocation policy disabled and media request denied. Backend/SDK/host suites cover additional boundaries below | Browser policy evidence only; native permission behavior unverified. |

## Backend traceability (16 local coverage groups)

These are organized coverage groups derived from the approved repository spec;
they are not a reconstructed verbatim numbering of the original XXII request.
Class names below are under `Viora.Application.Tests`.

| # | Boundary | Executable evidence |
| --- | --- | --- |
| 1 | Public DTO/private fields/default off | MiniAppProductionContractTests, CatalogEndpointTests |
| 2 | Immutable publication/eligibility | MiniAppProductionSnapshotTests, EligibilityTests, CatalogProjectionTests |
| 3 | Filter/count/page before pagination | MiniAppProductionCatalogPostgresTests |
| 4 | Backfill/migrations/retention | MiniAppProductionCatalogBackfillTests, CatalogMigrationTests, HostMigrationTests, AuditMigrationTests, RetentionPostgresTests |
| 5 | Real signed bearer/live source; no anonymous downgrade | MiniAppProductionAuthEndpointTests |
| 6 | Exact host/reserved origin/header policy | MiniAppProductionHostReservationTests, HostBoundaryTests, HostHeaderTests |
| 7 | Browser binding and nonce | MiniAppProductionBindingTests, HostEndpointTests |
| 8 | Account/source/anonymous receipt isolation | MiniAppProductionSecurityTests |
| 9 | Lost reply/idempotent grant/CAS | MiniAppProductionLaunchTests, GrantTests, GrantPostgresTests; real review replay in runner |
| 10 | Publication authority/rollback | MiniAppProductionLaunchPostgresTests, RuntimePostgresTests |
| 11 | Exact source-session lock/revocation race | MiniAppProductionRacePostgresTests: real concurrent PostgreSQL transactions; other source remains independent |
| 12 | Runtime credential/close isolation | MiniAppProductionSecurityTests, RuntimeTests |
| 13 | Live suspension and scoped SSO invalidation | RuntimePostgresTests; actual Flow C both client types |
| 14 | Configurable bounded quotas/events | MiniAppProductionRatePolicyTests, QuotaTests, EventTests |
| 15 | Audit/no credential formatting | MiniAppProductionAuditTests, RedactionTests; JSON grant contract intentionally retained |
| 16 | Private document/navigation/resource isolation | MiniAppProductionDocumentTests, ExternalLinkTests, AccessTests; Sandbox/SSO full backend regression |

## Frontend traceability (20 local coverage groups)

Paths abbreviated: `F` = frontend `features/mini-apps/tests`, `X` = backend
`tools/MiniAppProductionE2E`, `H` = `tools/ProductionHostTests`.

| # | Behavior | Evidence |
| --- | --- | --- |
| 1 | Catalog API contract/safe projection | F/catalog-client.test.ts |
| 2 | Loading/empty/error/retry | F/catalog-state.test.ts, catalog-ui.test.mjs; X/web-ui.e2e.mjs |
| 3 | Pagination/deduplication/reconnect | Same state/UI suites |
| 4 | Utilities placement and Wallet independence | F/utilities-integration.test.mjs; actual X/non-sso.e2e.mjs |
| 5 | Responsive widths/accessibility/keyboard | F/grid-layout.test.ts; X/web-ui.e2e.mjs at 320/375/768/1440 |
| 6 | Escaped text/logo behavior | F/catalog-ui.test.mjs; real logo observations in performance report |
| 7 | Anonymous exact route/root auth boundary | F/host-route-policy.test.ts; actual non-SSO deep link |
| 8 | Private prepare/launch transport | F/launch-client.test.ts |
| 9 | Stale/unmounted launch cancellation | F/host-controller.test.ts |
| 10 | Fresh close/reopen/replacement | F/host-controller.test.ts; X/web-host.e2e.mjs |
| 11 | Exact host iframe/source | F/web-host.test.mjs, host-transport.test.ts; H/transport.test.mjs |
| 12 | SDK v1 context/close compatibility | H/bridge.test.mjs; SDK bridge tests; real partner interaction |
| 13 | Forged bridge/private-channel messages | H bridge/transport suites; X/security.e2e.mjs |
| 14 | Navigation/dangerous schemes | H/navigation.test.mjs; SDK embedding/bridge tests; actual negative |
| 15 | Public SSO transaction/consent | SDK auth/popup tests; X/web-sso.e2e.mjs Public |
| 16 | Confidential backend session/consent | SDK partner-session tests; production-partner.test.mjs; actual Confidential |
| 17 | SSO reload/reopen/disposal | X/web-sso.e2e.mjs + web-host.e2e.mjs; H/lifecycle.test.mjs |
| 18 | Active suspend/recovery | X/moderation.e2e.mjs both clients |
| 19 | Web media denial | X/security.e2e.mjs; native already-granted permissions NOT VERIFIED |
| 20 | Root/Developer/Wallet compatibility | features/sso/bootstrap.test.ts; 63 Developer/BFF tests; Wallet card tests; actual Flow A |

## Delivery topics (21 local report groups)

| # | Topic | Status/reference |
| --- | --- | --- |
| 1 | Approved scope | Web milestone agreed 2026-10-23; native deferred |
| 2 | Architecture/authority | Spec sections 4–6; immutable reviewed publication |
| 3 | Storage/migrations | Additive migrations, isolated PG checks; no live migration |
| 4 | Catalog/filter/page | Two SQL reads in real catalog observation |
| 5 | Backfill | Operations runbook; operator dry-run before rollout |
| 6 | Utilities/Wallet | Actual Flow A; fixture boundary disclosed above |
| 7 | Responsive/accessibility | Exact measured Web widths; UI harness plus real host |
| 8 | Anonymous/auth routes | Signed source validation; anonymous deep link |
| 9 | Launch/redeem | Bound grant, replay and source race coverage |
| 10 | Dedicated Web host | Actual HTTPS export + separate host origin |
| 11 | SDK bridge | v1 kept; Sandbox v2 regression retained |
| 12 | Navigation/permissions | Web policy verified; native unverified |
| 13 | Public SSO | Actual embedded consent/exchange/reload/reopen |
| 14 | Confidential SSO | Actual backend exchange/fresh session validation |
| 15 | Moderation/recovery | Actual Flow C both clients |
| 16 | Security/redaction | Matrix above; private DTO formatting hardened |
| 17 | Audit/quotas/retention | Focused suites and full backend regression |
| 18 | Performance | [Measured observations](../testing/developer-platform-stage-8-performance.md), no benchmark claim |
| 19 | Build/regression/review | Final handoff; no high-severity finding in reviewed Web slices |
| 20 | Rollout/rollback/privacy | [Operations](../operations/developer-platform-stage-8.md); default off, exact origins, scoped disable/revoke |
| 21 | Platform/handoff | Web locally verified; Android/iOS NOT VERIFIED; no Stage 9/deploy/commit |

## Platform and acceptance limits

| Platform | Build/runtime evidence | Acceptance |
| --- | --- | --- |
| Web, isolated Chrome | Actual Expo export, TLS/issuer, real reviewed publications; Public/Confidential/non-SSO flows | Local Web milestone complete within the documented fixture boundary |
| Other browsers / deployed environment | Not executed | Rollout validation required, particularly third-party cookies and partner frame policy |
| Android | Debug assembly passes; Android 15 emulator now connected. Native runtime/security/SSO device evidence pending; see [native checkpoint](developer-platform-stage-8-native.md) | NOT VERIFIED |
| iOS | Windows host; no Mac/Xcode/device execution | NOT VERIFIED |

Native P0 still requires trusted wrapper/source proof, already-granted device
permission denial, real Public/Confidential SSO cookie/handoff feasibility and
device E2E. A broker/trust-boundary change needs its own reviewed design. No
external-browser fallback is counted as native host/bridge success.
