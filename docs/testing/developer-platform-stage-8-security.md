# Stage 8 security verification map — Web checkpoint

2026-10-23. User chose Web first, native not accepted. Latest embedded Public/
Confidential, non-SSO Utilities/Wallet and active suspension evidence supersedes
the earlier checkpoint below. See the [acceptance matrix](../handoffs/developer-platform-stage-8-acceptance.md)
for 16 backend / 20 frontend local coverage groups, platform limits and the
non-SSO fixture background-service limitation. Native permission/SSO remains
NOT VERIFIED; this is not full cross-platform acceptance.

Final checkpoint: **839 backend passed / 1 pre-existing MoneyAudit skip / 0
failed**; 41 frontend feature/Wallet/root SSO, 63 Developer/BFF, 37 SDK and 18
partner/cleanup/host tests pass. All three final Web business runners exit 0.
Release build, model-only check, root/fixture types and focused lint pass; known
warnings and raw evidence locations are recorded in the handoff.

Earlier continuation regression: 832 backend passed / one existing MoneyAudit
skip / zero failures, plus 13 focused quota tests and two owned-cleanup tests.
Earlier Public and Confidential real-host runners exited 0; UI-only Chrome checks,
34 feature/Wallet and 63 Developer/BFF tests pass. Evidence/limits in handoff.

| Boundary | Existing/new executable coverage |
| --- | --- |
| Immutable catalog; live filters before pagination | `MiniAppProductionCatalogTests`, `MiniAppProductionCatalogPostgresTests`, `MiniAppProductionCatalogProjectionTests` |
| Private fields/strict pagination/default off | `MiniAppProductionContractTests`, `MiniAppProductionCatalogEndpointTests` |
| Signed source; invalid bearer cannot become anonymous | `MiniAppProductionAuthEndpointTests.RealSignedBearerRequiresLiveSourceAndNeverFallsBackToAnonymous`, `MiniAppProductionLaunchEndpointTests` |
| Exact dedicated host, reserved issuer/Sandbox isolation and rollback | `MiniAppProductionHostReservationTests`, `MiniAppProductionHostBoundaryTests`, `MiniAppProductionHostHeaderTests` |
| Browser-bound one-use grant, replay/CAS rollback | `MiniAppProductionBindingTests`, `MiniAppProductionGrantTests`, `MiniAppProductionGrantPostgresTests` |
| Launch/network/authority/quota races | `MiniAppProductionLaunchTests`, `MiniAppProductionLaunchPostgresTests`, `MiniAppProductionRacePostgresTests`; latter proves exact source-session lock and revocation-before-redemption using real PostgreSQL |
| Live context/action invalidation and scoped close | `MiniAppProductionRuntimeTests`, `MiniAppProductionRuntimePostgresTests` |
| Audit integrity, migration/retention and Social preservation | `MiniAppProductionAuditTests`, `MiniAppProductionAuditMigrationTests`, `MiniAppProductionRetentionTests`, `MiniAppProductionWorkerTests` |
| Configurable scoped HTTP quotas | `MiniAppProductionRatePolicyTests` — four real TestHost limit/isolation checks plus five default/bounds cases |
| Binding/account history and session event quota | `MiniAppProductionQuotaTests` (1/20 limits, both source types); `MiniAppProductionEventTests` (1/10 limits), fixed-code rejection and unchanged context authority |
| Exact bridge/source/schema, private document generation and navigation | `tools/ProductionHostTests/{bridge,transport,lifecycle,navigation}.test.mjs`; native hostile-frame and permission evidence absent |
| Public screen and lifecycle UI | `tools/MiniAppProductionE2E/web-ui.e2e.mjs`: real UI/controllers, simulated services; measured widths and keyboard focus, not authorization proof |
| Real reviewed publication -> actual Expo Web host -> v1 SDK | `tools/MiniAppProductionE2E/run-fixture.mjs` + `web-host.e2e.mjs`, isolated PostgreSQL/TLS/issuer and actual review/approval/publication; Public and Confidential final runs exit 0 |
| Owned browser cleanup | `tools/MiniAppReviewE2E/owned-browser-cleanup.test.mjs`: stale missing-profile receipt is idempotent; existing unowned-PID profile preserved. Final Confidential runner exits 0 and fixed fixture ports closed afterward. |
| Cross-account/source/anonymous receipt and cross-browser runtime replay | `MiniAppProductionSecurityTests`: five cases; original credentials remain valid; revoked source produces no redeemed runtime/audit |
| Private credential formatting | `MiniAppProductionRedactionTests`: binding/nonce/grant absent from DTO ToString; required JSON wire fields preserved |
| Actual non-SSO + Wallet | `non-sso.e2e.mjs`: no Production client; actual Social login/WalletService/Utilities/anonymous+authenticated host/return/focus refresh; unrelated background APIs not registered in isolated fixture |
| Embedded Production SSO | `web-sso.e2e.mjs`, `production-partner.fixture.mjs`, four partner-boundary tests; Public SDK and Confidential backend, real deny/retry/repeat/new-scope/reload/reopen |
| Actual active suspension/recovery | `moderation.e2e.mjs`, both Public and Confidential: catalog/new launch/active host/token/held code denial; restore unpublished; explicit republish; Social preserved |
| Actual hostile Web bridge/media | `security.e2e.mjs`, both client variants: forged source/private channel cannot close live host, context remains valid; dangerous scheme and browser media policy denied. Not native permission proof. |

Runtime stays off by default. No production migration, email, deployment, commit,
native permission enablement or Stage 9 work is included. See
[handoff](../handoffs/developer-platform-stage-8.md) for executed results/limits.
