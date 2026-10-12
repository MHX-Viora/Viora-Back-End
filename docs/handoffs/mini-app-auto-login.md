# Mini App Auto Login — 2026-10-12

## Root cause verified in source

`MiniAppService.LaunchAsync` creates a host-private runtime and opens the published website. `useRuntimeBridge` only authorizes after a partner `requestOAuthLogin` request. Loading a URL does not create a partner login session and sharing ANKT cookies would not fix this architecture. SDK 2.1 had no automatic session synchronization contract. The example also trusted any authenticated partner cookie without checking which ANKT runtime/account opened it, allowing stale partner identity after an ANKT account switch.

The workspace contains an integration guide/demo for Nghệ Thuật Số, not the source of `nghethuatso.vn`. Its production behavior and deployment were not inspected or changed. Arbitrary websites still need the partner integration below; the host cannot turn an unintegrated login page into a real authenticated session.

## Delivered flow

1. A user taps a published Mini App. Host APIs use the existing authenticatedFetch refresh mechanism. A failed refresh returns to main login with a validated app UUID continuation; login/profile completion resumes that Mini App.
2. Backend checks account, app and Developer status; creates the existing hashed host-private runtime, now bound to published version. Independent apps open normally without automatic identity requests.
3. Native uses the isolated WebView. Web Auto Login reserves a partner window during the original tap, before async API checks lose popup permission; navigates it only to the verified published website. Deep links/popup blocking may require the user to open/allow the window. No reliance on third-party iframe cookies.
4. Exact origin/source/nonce/app/runtime handshake exposes a public runtime ID and the approved AutoLogin policy. It never exposes the host runtime secret or main JWT.
5. Partner calls SDK 2.2 `auth.autoLogin()`. A partner session bound to the same runtime is reused. A new runtime must authenticate again to establish the current main account; this conservative check can perform another silent OAuth round trip on reopening, without a password or repeated valid consent.
6. BFF creates state, nonce and S256 verifier; keeps verifier/secret/token server-side. Host checks current runtime, approved callback/scopes and existing consent. It asks only missing scopes, bringing the ANKT window forward for the consent dialog and returning focus to the partner after authorization where browser policy permits. Existing 60-second hashed OpenIddict handoff is one-use and bound to transaction, account, app, runtime and published version.
7. Provider redirects with code/state to the exact callback. BFF validates state, issuer/audience/nonce/signature/PKCE and signed ID-token `runtime_session_id` against its transaction, then rotates its HttpOnly session and serves the authenticated profile.
8. On account/runtime switch the BFF clears the prior identity before authorization; a denied/failed callback cannot keep serving the old account. Accounts use `(issuer, pairwise sub)`, never email matching.
9. Missing integration stops after 30 seconds with configuration guidance. SDK performs one automatic attempt per document; errors stop rather than loop. Host retry rearms the check and starts a fresh document/session as appropriate.

## Changed source files

Backend:
- `viora-BE/Viora.Application/MiniApps/MiniAppContracts.cs`: optional DTO/config AutoLogin fields.
- `viora-BE/Viora.Domain/Entities/MiniAppEntities.cs`: AutoLogin and runtime PublishedVersion.
- `viora-BE/Viora.Infrastructure/MiniApps/MiniAppConfigurationSnapshot.cs`, `MiniAppManagementService.cs`, `MiniAppService.cs`, `MiniAppService.Hybrid.cs`: draft/published mapping, validation, reviewed policy and runtime version checks.
- `viora-BE/Viora.Infrastructure/OpenApi/OpenApiAccessService.cs`: handoff version binding, signed runtime claim, token publication validation and active-runtime check at code exchange.
- `viora-BE/viora-BE/Controllers/OpenApi/OpenApiAuthorizationController.cs`, `OpenApiManagementView.cs`: code-exchange guard and truthful configuration/readiness metadata.
- `viora-BE/Viora.Infrastructure/Persistence/Migrations/20261012020223_AddMiniAppAutoLogin.cs`, `.Designer.cs`, `AppDbContextModelSnapshot.cs`; `viora-BE/deploy/open-api/mini-app-auto-login-migration.sql`.

SDK / partner:
- `viora-BE/viora-BE/wwwroot/mini-app-sdk/ankt-mini-app.js`: SDK 2.2 autoLogin, first-party opener bridge, bounded attempt/status reporting.
- `viora-BE/examples/mini-app-oidc/server.mjs`, `client.mjs`, `README.md`: runtime-bound callback/session, account-switch isolation, independent local logout and retry UX.
- `integrations/nghe-thuat-so-ankt/OPENAPI-OIDC.md`: actual partner integration requirements; no production source edits.

Host / Portal:
- `viora/features/mini-apps/use-runtime-bridge.ts`, `runtime-policy.ts`, `runtime-container.tsx`, `runtime-container.web.tsx`, `mini-app-tile.tsx`, new `partner-window.ts`.
- `viora/app/_layout.tsx`, `viora/features/auth/oauth-return.ts`, `login-screen.tsx`, `complete-profile-screen.tsx`: fixed UUID login continuation.
- `viora/types/mini-app.ts`, `types/developer.ts`, `services/developer.service.ts`, `features/developer/config.ts`, `app-form.tsx`, `open-api-panel.tsx`.
- `viora-admin/src/types/developer-platform.ts`, `services/open-api.service.ts`, `features/developer/config.ts`, `ConfigFields.tsx`, `OpenApiIntegrationPanel.tsx`.

Tests / reusable fixtures:
- BE `Viora.Application.Tests/OpenApiOAuthTests.cs`, `OpenApiManagementTests.cs`, `MiniAppHybridIntegrationTests.cs`, `tests/open-api-sdk.test.cjs`.
- Partner new `auto-login.test.mjs`, `client.test.mjs`, `browser-fixture.mjs`; extended `server.test.mjs`.
- Expo extended runtime bridge/web controls, config, Utilities grid and OAuth return tests; new `services/mini-app-auth-refresh.test.mjs`.
- Admin `tests/mini-app-config.test.mjs`.
- Spec/ordered plan: `docs/specs/mini-app-auto-login.md`.

## Database and rollout

Additive migration adds `MiniApps.AutoLogin boolean DEFAULT false` and `MiniAppRuntimeSessions.PublishedVersion integer DEFAULT 0`. No duplicate launch/consent/account-link/SSO table. OpenApiDbContext uses existing OpenIddict properties; no protocol DB migration needed.

SQL is idempotent against the preceding `20261012012833_AddDeveloperPlatformLifecycle` migration and was generated/reviewed, not applied. Existing runtime rows at version zero fail validation for apps at a later published version; reopening creates a current runtime. Previously issued partner tokens without the new published-version claim are rejected by the new validation; coordinate deployment and allow automatic reauthorization.

Operator sequence:
1. Review/apply prior lifecycle migration and new SQL on dedicated staging PostgreSQL; run PostgreSQL concurrency suite with `VIORA_MINI_APP_TEST_DB`. No production database was touched.
2. Deploy BE/Open API and SDK 2.2, then the Expo/Admin builds with the existing public issuer configuration. Verify callback domains, TLS, issuer keys, proxy boundaries and sanitized proxy/APM logging (no OAuth query/body/header secrets).
3. Integrate the partner BFF into the real website and durable transactional shared session/account stores. The runnable demo uses RAM maps and is not a production multi-instance persistence system.
4. Portal: select SSO Optional/Required, callbacks/origins and minimum scopes; enable Auto Login in a draft, test, submit for review, approve and publish. Existing applications remain opt-in; no unapproved application is enabled automatically.
5. Test actual callback → protected partner content. Metadata reports configuration/domain verification and mandatory PKCE, but keeps `SilentSsoStatus=PartnerVerificationRequired`: domain verification does not prove the partner has implemented SDK/callback/account linking. Do not treat it as ready until the real deployment passes.
6. Validate COOP/opener and popup behavior on Chrome/Safari; native isolation builds on Android/iOS. Only then promote to production with normal operational approval.

## Partner integration contract

Load `/mini-app-sdk/ankt-mini-app.js?v=2.2`, configure exact HTTPS host origin and same-origin BFF paths. In the host/native/opener context call `await ANKT.auth.autoLogin()` at startup, including when a partner cookie exists. Standalone website login can keep its button and `auth.login()`.

`GET /auth/ankt/session`: CSRF, issuer/host metadata, authenticated and runtimeSessionId derived from a validated ID token. `POST /auth/ankt/start`: Origin + CSRF, public appId/runtimeSessionId; store state/nonce/verifier/runtime binding. Callback: use maintained OIDC library, consume transaction, compare signed runtime_session_id, rotate HttpOnly session. Never trust the browser-supplied runtime ID by itself. `GET /auth/ankt/profile` checks provider UserInfo on protected actions and returns no tokens. `/auth/ankt/logout` deletes the local session and attempts token revocation.

No arbitrary session/logout URL is introduced: registered OAuth callback establishes the BFF session; logout uses the configured same-origin backend path. Existing ANKT unlink/consent revocation remains supported. Linking to an existing partner account requires ownership verification in the actual website, not an email comparison or a demo Map.

Main logout closes the host runtime/window; partner logout does not log out ANKT. Partner sessions have their own short lifetime (demo max 300 seconds) and provider validation on protected actions. This is not global single logout. If provider revoke is unavailable, local logout still succeeds and reports `providerRevoked:false`; the provider token remains valid until expiry/revocation, and UI says so.

## Verification and mandatory scenario coverage

| Scenario | Evidence |
|---|---|
| 1–2 Logged main user, existing consent → automatic session | SDK auto flow; real HTTP BFF + real openid-client signed-provider integration; browser popup authenticated profile. .NET real code flow and reused consent. |
| 3 Missing consent → only missing scopes | AutoLoginNeverGrantsConsentAndAsksOnlyMissingScopes; host bridge consent tests. |
| 4 Independent app gets no automatic identity | SDK independent case, host READY policy, backend published policy rejection. |
| 5 Expired main session | Actual authenticatedFetch refresh behavior with controlled transport; safe UUID continuation tests. Main login UI uses existing email/Google flows. |
| 6–7 Replay/expiry | Real OpenIddict handoff/code replay and expiry; BFF callback replay/expiry. |
| 8–10 App/callback/PKCE isolation | Real OAuth second-client, exact redirect, wrong verifier tests; bridge source/origin/app/session/nonce guards. |
| 11–12 Suspended app / revoked Developer | Existing suspension tests and RevokedDeveloperStopsAutomaticLoginAndExistingPartnerTokens. |
| 13 Partner token cannot access internal APIs | Real separate-issuer/audience and internal financial/chat/admin boundary tests. |
| 14 Account switch | Real SDK/BFF signed-provider integration + browser A→B; new start clears prior profile before callback. |
| 15 Logout policy | CSRF/local session removal/provider failure regression; browser logout removes profile, main synthetic host remains A; retry button works. |
| 16 No token/secret leak | Host-private READY/response assertions, SDK payload checks, BFF browser JSON checks; backend logging filters and audit omit sensitive protocol values. Proxy/APM needs operator verification. |
| 17 Partner not integrated | Deterministic missing-SDK timeout test → instructions/no grant; readiness metadata never asserts fabricated partner success. |
| 18 Failure/retry | Bridge REQUEST_FAILED/denial tests, launcher retry UI, bounded SDK auto attempt; browser logout/relogin and regression for retry button. |

Final automated totals: 177 BE passed, 2 PostgreSQL-dependent tests skipped; 77 Expo focused tests, 24 Admin tests, 33 SDK/partner tests passed (311 passed overall). Targeted follow-ups ran after the last small edits; totals count distinct tests, not repeated runs. SQLite/localhost fixtures never use production data.

BE Release build (0 errors/warnings), Admin build, Expo TypeScript/scoped lint and Expo Web export passed. Admin has its existing >500KB bundle warning. New migration is additive. Diagnostic logs/artifacts are under root `.diagnostics`.

Browser: SDK 2.2 + real partner BFF + real maintained OIDC validation, synthetic ANKT host/provider and test TLS. Verified direct authenticated content, pairwise account A→B, local logout, manual retry and clean partner console. This is not a production-issuer E2E, nor a browser run of the production React host with production credentials. The production React host was covered by component/bridge tests and Web export.

Android/iOS: native bridge/injection/isolation code and shared tests reviewed; no device/simulator run or native rebuild here. Need actual isolation-plugin builds and device testing before claiming platform completion. PostgreSQL concurrency and real partner deployment checks remain explicit operator work.

## Review

Checked reviewed-versus-draft policy, independent isolation, account-switch failure behavior, exact source/origin and nonce, absence of host JWT/secret in partner payloads, one-use handoff, PKCE/callback binding, runtime close/version invalidation, no implicit consent, expiry/revocation and clear error/retry behavior. Preserved unrelated existing WIP; no commit, database update, deployment or external messaging.
