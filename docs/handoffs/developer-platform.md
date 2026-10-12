# ANKT Developer Platform — verification and handoff

Date: 2026-10-12 (Asia/Saigon). Source completion and local verification; no production deployment or database updates.

## Actual architecture

- `viora-BE`: .NET 8, EF Core/PostgreSQL; existing Account/User, Developer/Membership, MiniApp/Version/Permission/Domain/Consent, notification and audit entities remain authoritative.
- `viora`: Expo SDK 54 / React Native/Web; existing authenticated ANKT session, theme tokens, upload service, marketplace and isolated WebView runtime are reused.
- `viora-admin`: React/Vite; existing admin authentication and review pages plus the Developer Portal. Developer tokens and Admin tokens retain their separate existing client namespaces.
- OpenIddict protocol storage uses the same database with its separate context/history. Partner validation, asymmetric keys, issuer/audience, scopes and public-host routing are separate from the internal JWT scheme.
- Existing working changes were already present at the start. They were preserved and verified; this handoff does not attribute all platform code to this session.

## Working source modules

| Requirement | Source / behavior |
| --- | --- |
| Developer registration | `Viora.Infrastructure/MiniApps/DeveloperPlatformService.cs`; draft, OTP, current terms, immutable submission, pending status/history |
| Admin Developer review | `AdminDevelopersController`, `DeveloperReviewPage`; active Admin, exact version, public reason/private notes, concurrency token |
| Portal / account entry | `viora/features/developer`, `viora-admin/src/features/developer`, existing account settings entry |
| Mini App wizard / management | Config DTOs, six-step Expo wizard, Admin portal, owner/team guards and scoped services |
| Review / publication | Technical test bound to configuration hash; submitted snapshots immutable; separate review decision and publication; old published version survives draft changes |
| Domain / URL security | Verified exact domains/callbacks; public HTTPS; SSRF probe pins validated DNS addresses, disables redirects/proxies/cookies, limits timeout/headers |
| Team | Owner/Admin/Developer/Analyst/Viewer capability checks; one-use reauth for team/domain/secret operations |
| SSO / Open API | OpenIddict code + S256 PKCE, consent/live registry checks, revocation, audience/client separation, rate limits and sanitized diagnostics |
| Runtime / discovery | Existing public marketplace and web/native isolation/bridge checks; suspended/revoked registry state blocks runtime privileges |
| Notifications | Existing persisted System notifications; readable Vietnamese; tokens/secrets/private notes excluded |
| Testing mode | `MiniAppTechnicalProbe`, `RunTechnicalTestAsync`; explicitly **Limited**, HTTPS/configuration checks, no synthetic production token issuance |
| Integration / operations | Existing `viora-BE/docs/open-api-partner-examples.md`, `docs/open-api-deployment.md`, `deploy/open-api/*.example`, executable OIDC partner example |

Owner transfer and MFA recovery are not enabled; do not imply support by allowing users to assign Owner. Webhook delivery and logout redirect registration are not enabled. No independently isolated Sandbox, legal-document intake, verified organization legitimacy, synthetic production test identities, or full browser/native compatibility certification is claimed.

## Changes completed in this session

- Fixed an uncompilable test using `AccountStatus.Suspended`; locked accounts use the existing `Banned` value.
- Updated existing regression fixtures to perform the real required technical-test/submission workflow, with an injected network probe and deterministic cooldown handling.
- Fixed Expo registration status typing and CommonJS test resolution. Reworked registration/app wizard tests to exercise current multi-step behavior, OTP and accepted terms before submission.
- Restored corrupted Vietnamese terms, notification and verification-mail text; email explicitly uses UTF-8. Added a failing-then-passing regression for readable, versioned consent documents.
- Fixed Admin legal-profile validation: organizations need a website; supplied websites require HTTPS. Added regression coverage.
- Added Developer Portal/login Light/Dark control using existing CSS tokens/theme preference; synchronized confirmed email in the saved-profile display.
- Generated additive `AddDeveloperPlatformLifecycle` migration, model snapshot and idempotent SQL. Corrected legacy defaults/backfill so existing registrations and approved releases keep their meaning.
- Cleared Developer reauth/bridge lint warnings without changing runtime protocol behavior.
- Audited production dependencies; applied compatible security patches. Final audit counts are recorded below after verification.

## Database deliverables

`viora-BE/Viora.Infrastructure/Persistence/Migrations/20261012012833_AddDeveloperPlatformLifecycle.cs` (+ Designer and AppDbContextModelSnapshot) and `viora-BE/deploy/open-api/developer-platform-migration.sql`.

- Adds `DeveloperApplications`, `DeveloperReviewHistory`, `DeveloperEmailVerifications`, `DeveloperReauths`, `MiniAppTechnicalTests` with foreign keys/indexes. Existing registry tables get profile/version/metadata columns.
- Existing Developers use `RegistrationStatus=Legacy` and `PartnerType=Individual`; existing SSO metadata is derived from AuthenticationMode. Existing version review state is derived from Status. No existing account/developer/app rows are deleted.
- Generated Up SQL is additive plus metadata backfill; no DROP TABLE, DROP COLUMN or destructive DELETE. EF reports no pending AppDbContext model changes.
- Open API migration remains `Viora.Infrastructure/OpenApi/Migrations/20261010082214_AddOpenApiProtocolStore.cs`, SQL `deploy/open-api/open-api-migration.sql`, separate migration history.
- SQL was generated/inspected only. Existing data and lock behavior require staging PostgreSQL verification. Down removes new storage; do not use rollback after accepting new submissions without a backup/export.

## Verification

- Backend: 172 passed, 2 skipped / 174 total. Tests include actual OpenIddict issuance/redemption, PKCE/client/callback binding, audience boundaries, suspension/revocation, ownership/roles, OTP, snapshot immutability and consent.
- Backend Release build: 0 warnings/errors. EF model check passed; known pre-existing ReportReason sentinel warning is separate from build.
- Admin: 23 tests; production build and scoped lint passed. Existing bundle-size advisory remains.
- Expo: 70 focused tests; full TypeScript and Developer/MiniApp scoped lint passed; web export generated locally.
- Post-patch check against the installed Expo SDK's `bundledNativeModules.json`: zero direct dependency version mismatches.
- SDK: 17 tests. Executable partner OIDC example: 8 tests.
- Browser: actual Admin Portal on localhost with intercepted synthetic API responses; exercised draft save, blocked unverified progression, incorrect/correct OTP, accepted terms, submit and read-only PendingReview. This is component runtime verification, not production API E2E.
- Mobile viewport 390x844: no horizontal overflow; Light/Dark toggles change existing semantic tokens. Browser console has no errors/warnings in checked flow.
- Dependency audit: Admin **0 vulnerabilities**, including build dependencies after compatible fixes (Axios 1.20.0, React Router DOM 7.18.4). Expo production tree reduced from 59 (including 1 critical) to **52: 34 high, 18 moderate, 0 critical**. Shell-quote patched 1.9.0 → 1.12.0; Expo stays SDK 54 (54.0.35 → 54.0.37), React Native 0.81.5, Expo Router 6.0.24 and Firebase 12.15.0 remain compatible. Remaining advisories include Firebase/gRPC and Expo tooling chains; do not treat the audit as proof that every vulnerable path is exercised in the browser/native bundle. No `--force`, major downgrade or lifecycle script execution was used.
- Two PostgreSQL tests require dedicated `VIORA_MINI_APP_TEST_DB` and `VIORA_MONEY_AUDIT_TEST_DB` databases. No production connection was used for tests.

## Operator steps and remaining release checks

1. Back up the existing database; run the new migration against a staging copy and verify old records, indexes and concurrency under PostgreSQL before approval to apply production SQL.
2. Configure SMTP/TLS sender and independent `DeveloperVerification__OtpHashKey` (at least 32 UTF-8 bytes) from `deploy/open-api/developer-platform.env.example`. Confirm actual delivery/expiry/rate limits. API returns explicit 503 while configuration is missing.
3. Provision persistent Open API signing/encryption certificates, issuer/audience/authorization UI, trusted proxies, DNS and TLS using existing Open API deployment guide. Feature defaults off until configured.
4. Deploy backend/Expo/Admin versions together only after approval. Rebuild native clients to include WebView isolation plugin changes; verify Android/iOS, browser cookie restrictions, real partner callback and no bridge/token leakage on target devices.
5. Limited tester is not a security review or isolated Sandbox. Admin must review content/age/region/permissions and actual embedded behavior before publication.
6. Keep Owner transfer, legal-document intake and webhook features unavailable until their workflows/private storage/MFA/delivery policies are implemented.
7. Audit log retention follows existing operations policy. OTP/reauth cleanup is hourly and does not delete review history.
8. Resolve remaining Expo advisories with a separately verified SDK/dependency migration and native build checks before claiming a clean dependency security result. Automated `npm audit fix --force` currently proposes incompatible SDK/Firebase changes and was not run.

## Main source paths changed here

BE: `DeveloperPlatformService.cs`, `DeveloperVerificationMailer.cs`; tests `DeveloperPlatformTests.cs`, `MiniAppPlatformTests.cs`, `MiniAppHybridIntegrationTests.cs`, `MiniAppAdminReviewTests.cs`, `OpenApiOAuthTests.cs`; migration/Designer/model snapshot and generated SQL.

Expo: `features/developer/platform-policy.ts`, `profile-form.tsx`, `reauth-dialog.tsx`, `config.test.mjs`, `ui.test.mjs`; `features/mini-apps/runtime-bridge.test.mjs`; dependency lockfile.

Admin: `DeveloperPortal.tsx`, `DeveloperThemeButton.tsx`, `DeveloperRegistrationWizard.tsx`, `registration.ts`; `tests/developer-registration.test.mjs`, `developer-platform.test.mjs`, `developer-browser-fixture.js`; dependency lockfile.

Diagnostic output is local under `.diagnostics/`; build artifacts are not deliverable source.
