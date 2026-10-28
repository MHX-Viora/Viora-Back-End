# Stage 10 — architecture and authority map

2026-10-23. Source review plus executed local suites; deployed configuration is
unverified. Paths below are relative to BE unless prefixed `../viora` or Admin.

| Component | Entry / implementation | Authority / verification |
| --- | --- | --- |
| Developer account | `viora-BE/Developer/DeveloperApi.cs`, `Viora.Infrastructure/Developers` | Independent account/session, persisted expiry/revocation; `DeveloperApiTests`, `DeveloperIdentityTests` |
| Portal | `../viora/features/developer`, `app/developers` | Integrated Expo routes, Console validates backend session; fresh account/docs browser E2E |
| BFF | `../viora/server/developer/handler.cjs`, `account-proxy.cjs`, `developer-cookie.cjs` | Allowlisted transport, origin/header/body validation, HttpOnly Secure cookie, no session returned to browser; 63 tests |
| Admin | `viora-BE/Admin`, `../viora-admin/src/services/admin-mini-app.service.ts` | Signed Admin session plus module authorization and live source; actual password-login/browser review |
| Registry | `MiniAppService`, `MiniAppSsoService`, `MiniAppSecretProtector` in Infrastructure/Developers | Owner-scoped clients; versioned AES-GCM secret protection; rotation/revocation tests |
| Domain | `MiniAppIntegrationService`, `MiniAppDomainLifecycle`, public web fetch/address services | Proof bound to hostname/environment; SSRF/DNS/redirect checks; domain/network suites and owned HTTPS proof |
| Issuer | `viora-BE/Sso/SsoRegistration.cs`, authorization/token controllers | OpenIddict code+S256; exact issuer, callback, environment, signed identity; protocol HTTP tests and real browser |
| Registry projection | `Viora.Infrastructure/Sso/MiniAppOidcRegistry.cs`, `SsoClientPolicy.cs` | Registry credential authority; projection does not grant approval/publication; exact environment/redirect/scope checks |
| Consent | `SsoConsentService`, `SsoConsentPage`, session endpoints | Subject derives from live signed account/session; grant/revocation and scope checks; consent tests |
| Account link | `SsoAccountLinkService` and endpoint tests | Separately authenticated Developer/Social provenance; no browser-supplied subject trust |
| SDK / Public partner | `../viora/packages/miniapp-sdk` | PKCE/state/nonce/ID token/UserInfo, no Public secret, bounded one-use popup; 37 SDK tests + Public E2E |
| Confidential partner | `tools/MiniAppReviewE2E/partner.fixture.mjs`, `production-partner.fixture.mjs` | Backend exchange/HttpOnly partner session; Confidential E2E. This fixture does not certify the docs .NET middleware sample |
| Sandbox | `MiniAppSandbox*` services/APIs, `tools/SandboxExecutor` | Browser observation is not trusted executor evidence; owned executor checks and fresh real 18-check runs |
| Version | `MiniAppVersion*` services/configuration/tests | Immutable submitted snapshots; draft changes cannot mutate approved/live version; PG race tests |
| Review | `MiniAppReview*`, `MiniAppReadinessService` | Transactional live authority revalidation, assignment/decisions/outbox; PG + actual Admin browser |
| Publishing/moderation | `MiniAppPublishingService`, `MiniAppModerationService` | Approval is separate from publication. Suspension revokes live launch/token authority; restore leaves unpublished |
| Catalog / production host | `viora-BE/Production`, `MiniAppProduction*` | Immutable publication projection; eligibility before pagination; source-bound one-use grants, browser binding, dedicated host/origin and revocation locks |
| Native / docs / operations | `../viora/features/mini-apps/adapters/host.native.tsx`, `modules`, `features/developer/docs`, workflows | Native gate stays closed. Static docs require composed export. `/health` liveness and `/health/ready` DB connectivity now separate |

Transactions use current owner/source status, immutable target and optimistic
revisions before committing. PostgreSQL-specific locks, unique indexes and
immutability triggers supplement application checks. See the database report.
SQLite tests supplement, rather than replace, PG concurrency/constraint proof.

Developer, Social and Admin sessions remain separate; Sandbox and Production
clients/hosts cannot substitute for one another. SDK validation does not replace
backend authority. Native browser-to-WebView SSO trust is still the Stage 8 P0;
no new broker/session handoff was introduced.

Operational gaps: the FE deployment workflow exports Expo without composing the
static documentation artifact; main startup has no explicit `UseForwardedHeaders`
call. Neither observation proves the actual VPS behavior. Verify trusted proxy
configuration, HTTPS issuer processing, BFF and docs routing in staging before GO.
