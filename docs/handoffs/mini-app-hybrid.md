# ANKT Hybrid Mini App handoff

Implemented across `viora-BE`, `viora-admin` and `viora`, 2026-10-09. No production deployment, database update or commit was performed. Earlier unrelated working-tree changes were preserved.

## Result and authentication

- Independent mode opens the registered partner website; its passwords, accounts, cookies and login remain partner-owned.
- ANKT SSO is explicitly requested through the guarded SDK. Partner state/browser transaction + S256 PKCE + exact callback + runtime binding protect the 60-second one-use code exchange. Partners issue their own session after receiving a pairwise identity.
- Identity login is required; optional profile claims can be declined. No ANKT access/refresh/runtime token or client secret is exposed to embedded pages.
- Runnable Node/.NET examples create local users or explicitly link an authenticated existing user, reject identity conflicts and support confirmed unlink while preserving account data. No email auto-linking.

## Screens and APIs

Developer Portal `/developer`: separate login, onboarding/profile, overview, owned apps, five-step Draft wizard, details/configuration, domain challenges, SSO instructions, permissions, owner-managed team, analytics and audit. Draft creation precedes verification and explicit review submission.

Admin: existing apps/Developers extended with creation/account linking, pending versions, version snapshots and decision reasons, suspend/reactivate/archive, categories, permission catalogue, reports and audit. Developer/admin refresh cannot switch principals or revive a logged-out session.

User: `/mini-apps` marketplace and `/mini-apps/[id]` details/runtime, API-fed search/category/featured, account-scoped recents/favorites, consent/revoke and reports. Native toolbar provides back/refresh/close/external browser; web iframe provides refresh/close/external tab.

| API root | Added/extended behavior |
|---|---|
| `/api/mini-apps` | Active published registry/detail, categories, launch, session validate/close, authorize, reports |
| `/api/mini-apps/{id}/consent` | Approved scope grant/revoke; active runtime reflects current grants |
| `/api/mini-app-auth/exchange` | Client auth, state/exact callback/PKCE/runtime, expiry/atomic one-use redemption, pairwise scoped identity |
| `/api/developer/profile`, `/team` | Self-onboarding/profile and owner-only membership management |
| `/api/developer/mini-apps` | Owned configuration, submit/rotate, exact-host proof, immutable versions, analytics/audit |
| `/api/admin/mini-apps`, `/developers` | Creation, version moderation, emergency status changes, policy/reports/audit |
| `/api/admin/mini-app-categories` | Category management |

Exact DTOs and authorization contracts are in `Viora.Application/MiniApps/MiniAppContracts.cs`; developer and admin API services use those contracts.

## Migration and security

Migration `20261009081149_AddHybridMiniAppPlatform` adds six tables (memberships, categories, versions, verified domains, runtime sessions, reports), twelve binding/configuration fields, indexes and backfills. Legacy app status enum values and data remain intact; existing apps retain `AnktSso`, while new wizard apps default to `Independent`. Published version snapshots isolate unreviewed edits.

Generated review-only SQL: [.codex-tmp/mini-app-hybrid.sql](../../.codex-tmp/mini-app-hybrid.sql). The artifact is local build output; the C# migration/designer/snapshot are source files. No application database connection was needed to generate/compare the model.

Existing Active/Suspended sites remain launchable after migration. Trusted embedded origins and SSO callback authorization require exact verified hosts; legacy pending apps must verify before approval. Legacy codes lacking new bindings cannot redeem.

Domain verification uses bounded public HTTPS, validates all DNS answers, pins the actual socket, verifies TLS and disallows redirects/proxies/private IPs. Bridge checks method/schema/source/origin/main-frame/app/session/nonce, replay, size/rate, expiry and current backend status. Native delivery additionally checks destination document binding after queued injection.

Pinned `react-native-webview` 13.15.0 has reproducible scoped source patches via postinstall/Expo plugin: deny Mini App capture, file chooser/downloads and unproven child-frame/legacy bridge transport. Existing call/camera WebViews are outside the marker. Unknown upstream source fails the patch rather than silently weakening protection. Native capability attestation is required; old binaries/Expo Go/unsupported transport use browser fallback.

## Verification and remaining work

| Verification | Result |
|---|---|
| Backend solution build | 0 warnings/errors |
| Backend tests | 101 passed, 2 PostgreSQL-dependent fixtures skipped; hybrid subset 45 passed + 1 skipped |
| Migration | EF model has no pending changes; generated SQL reviewed as additive; not applied |
| SDK | 7 backend-source checks + 6 independent source VM checks pass |
| Expo | TypeScript/lint pass; 18 runtime/policy/native-patch/injection checks pass; web export succeeds |
| Admin | Build/lint pass; 35 tests pass (14 new, 21 existing); bundle-size warning remains |
| Partner examples | Node 11 tests; ASP.NET build + 7 tests pass |
| Live browser/devices | No connected browser runtime or ADB device; walkthrough/native compile/device behavior unverified |

This is an implemented and automatically verified platform, not a production-ready certification. Release work still requires migration rehearsal/real PostgreSQL concurrency, native Android/iOS rebuild and device acceptance, HTTPS/domain proof and end-to-end partner flows, browser/cookie/OAuth checks and normal staging/deployment controls. No generic external-browser OAuth session transfer or iframe-policy bypass is promised.

Embedded camera/microphone/upload/download capabilities remain unavailable. The catalogue accepts only existing seven identity/profile/app metadata scopes; supported grants persist until revoked. Adding sensitive native APIs and per-session consent requires a separate implementation and device validation. ANKT consent revoke does not delete partner users or automatically revoke sessions the partner already issued.

Read [testing](../MINI_APP_TESTING.md) for reproducible commands/acceptance and [deployment](../MINI_APP_DEPLOYMENT.md) before release. The nine topic documents and runnable examples cover architecture, developer/admin operation, integration, SSO, SDK, security, testing and deployment.

## Changed source files

Follow-up clarified: personal profile → settings/activity menu → **Developer Portal**, below Advertisements, on native and desktop/web. Opens `/developer` INSIDE ANKT, using the current ordinary user session. Unregistered users see profile registration; Pending users see status; Active developers create/edit their own apps, prove domains, submit review, view status/versions/usage/audit and integrate SSO/rotate owner secrets. It calls only public/developer APIs, not admin routes. The earlier external opener and its URL setting have been removed.

Additional follow-up files: `viora/components/profile/profile-settings-sheet.tsx`, `viora/features/profile/profile-screen.tsx`, `viora/app/developer/{index,apps/new,apps/[id]}.tsx`, `viora/features/developer/{shared,profile-form,dashboard-screen,app-form,credentials-dialog,create-screen,app-screen}.tsx`, `viora/features/developer/config.ts`, three developer test files, `viora/services/developer.service.ts`, `viora/types/developer.ts`, `viora/docs/specs/developer-mini-app-management.md`; spec/plan/guides updated accordingly. New focused tests:13 pass; combined Developer/profile/runtime regression run:36 pass. TypeScript/lint/web export and browser availability are recorded in the in-app handoff.

The inventory below excludes generated build output and pre-existing unrelated changes.

### viora-BE

~~~text
docs/handoffs/mini-app-hybrid.md
docs/handoffs/mini-app-platform.md
docs/handoffs/mini-app-verification.md
docs/MINI_APP_ADMIN_WORKFLOW.md
docs/MINI_APP_ARCHITECTURE.md
docs/MINI_APP_DEPLOYMENT.md
docs/MINI_APP_DEVELOPER_GUIDE.md
docs/MINI_APP_INTEGRATION.md
docs/MINI_APP_SDK.md
docs/MINI_APP_SECURITY.md
docs/MINI_APP_SSO.md
docs/MINI_APP_TESTING.md
docs/plans/mini-app-hybrid.md
docs/specs/mini-app-hybrid.md
examples/mini-app-dotnet/PartnerDb.cs
examples/mini-app-dotnet/PartnerMiniApp.csproj
examples/mini-app-dotnet/Program.cs
examples/mini-app-dotnet-tests/IdentityTests.cs
examples/mini-app-dotnet-tests/PartnerMiniApp.Tests.csproj
examples/mini-app-node/identity-store.mjs
examples/mini-app-node/identity-store.test.mjs
examples/mini-app-node/server.mjs
examples/mini-app-node/server.test.mjs
examples/mini-app-sdk.test.mjs
examples/README.md
tests/mini-app-sdk.test.cjs
Viora.Application.Tests/MiniAppHybridIntegrationTests.cs
Viora.Application.Tests/MiniAppHybridPostgresTests.cs
Viora.Application.Tests/MiniAppHybridSecurityTests.cs
Viora.Application/MiniApps/MiniAppContracts.cs
Viora.Application/MiniApps/MiniAppSecurityPolicy.cs
Viora.Domain/Entities/Enums.cs
Viora.Domain/Entities/MiniAppEntities.cs
Viora.Infrastructure/DependencyInjection.cs
Viora.Infrastructure/MiniApps/MiniAppConfigurationSnapshot.cs
Viora.Infrastructure/MiniApps/MiniAppDomainVerifier.cs
Viora.Infrastructure/MiniApps/MiniAppExpiryCleanup.cs
Viora.Infrastructure/MiniApps/MiniAppManagementService.cs
Viora.Infrastructure/MiniApps/MiniAppManagementService.Hybrid.cs
Viora.Infrastructure/MiniApps/MiniAppService.cs
Viora.Infrastructure/MiniApps/MiniAppService.Hybrid.cs
Viora.Infrastructure/Persistence/AppDbContext.cs
Viora.Infrastructure/Persistence/AppDbContextFactory.cs
Viora.Infrastructure/Persistence/Configurations/MiniAppConfigurations.cs
Viora.Infrastructure/Persistence/Migrations/20261009081149_AddHybridMiniAppPlatform.cs
Viora.Infrastructure/Persistence/Migrations/20261009081149_AddHybridMiniAppPlatform.Designer.cs
Viora.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs
viora-BE/Controllers/Admin/AdminMiniAppCategoriesController.cs
viora-BE/Controllers/Admin/AdminMiniAppsController.cs
viora-BE/Controllers/Developer/DeveloperMiniAppsController.cs
viora-BE/Controllers/Developer/DeveloperProfileController.cs
viora-BE/Controllers/MiniAppsController.cs
viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js
~~~

### viora-admin

~~~text
src/App.tsx
src/components/common.tsx
src/features/developer/config.ts
src/features/developer/ConfigFields.tsx
src/features/developer/context.ts
src/features/developer/CreateMiniAppPage.tsx
src/features/developer/developer.css
src/features/developer/DeveloperAppPage.tsx
src/features/developer/DeveloperPortal.tsx
src/features/developer/DeveloperToolsPages.tsx
src/features/developer/Shared.tsx
src/pages/AdminMiniAppCreatePage.tsx
src/pages/DevelopersPage.tsx
src/pages/MiniAppDetailPage.tsx
src/pages/MiniAppPolicyPage.tsx
src/pages/MiniAppsPage.tsx
src/services/admin-mini-app.service.ts
src/services/auth.service.ts
src/services/developer.service.ts
src/services/developer-auth.service.ts
src/types/mini-app.ts
tests/mini-app-auth.test.mjs
tests/mini-app-config.test.mjs
tests/mini-app-ui.test.mjs
~~~

### viora

~~~text
app.json
app/mini-apps/[id].tsx
app/mini-apps/index.tsx
features/mini-apps/consent-dialog.tsx
features/mini-apps/detail-screen.tsx
features/mini-apps/marketplace-screen.tsx
features/mini-apps/preferences.ts
features/mini-apps/report-form.tsx
features/mini-apps/runtime-bridge.test.mjs
features/mini-apps/runtime-container.tsx
features/mini-apps/runtime-container.web.tsx
features/mini-apps/runtime-injection.test.mjs
features/mini-apps/runtime-injection.ts
features/mini-apps/runtime-policy.test.mjs
features/mini-apps/runtime-policy.ts
features/mini-apps/use-runtime-bridge.ts
features/utilities/utilities-screen.tsx
package.json
package-lock.json
plugins/with-mini-app-isolation.js
plugins/with-mini-app-isolation.test.cjs
scripts/apply-mini-app-isolation.cjs
services/mini-app.service.ts
types/mini-app.ts
~~~
