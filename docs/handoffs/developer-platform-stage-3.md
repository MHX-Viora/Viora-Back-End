# ANKT Developer Platform — Stage 3

Implemented 2026-10-16. Scope: real Mini App management and confidential client credentials. No production migration/deployment, ANKT social auth changes, sandbox runtime, authorization/token endpoint, domain verification or review submission.

## Architecture

Uses the existing .NET 8 API, shared AppDbContext/Npgsql database, independent verified DeveloperAccounts and DeveloperSession authentication. The portal is the existing root Expo/React web feature, with same-origin Node BFF and HttpOnly cookies. The historical Next portal is superseded. Current routes remain under `/developers` on the existing origin; setting up `developer.ankt.vn` and changing the prefix/domain is deployment work outside this change.

Ownership comes exclusively from authenticated `developer_id`. Every app operation filters by owner and soft-deletion state; inaccessible resources return 404. ANKT Bearer JWTs cannot authorize these endpoints. BFF retains exact-origin/custom-header CSRF checks, cookie isolation, request bounds, trusted IP signing, no-store and sanitized failures.

## Schema and migration

Migration: `20261016013639_AddDeveloperMiniAppManagement`; six additive tables, restrictive foreign keys, no cascade deletions. Historical removed Mini App migrations are untouched. Configured production database was not queried or changed in this stage; current source/snapshot and the previous removal migration establish the baseline. Review actual deployed migration history before applying.

| Table | Fields / constraints |
| --- | --- |
| MiniApps | UUID PK; unique immutable AppId; DeveloperAccount FK/index; name/category/logo/descriptions/website/support/policy/terms; status index; UTC timestamps; DeletedAt; unique owner+CreateRequestId and fingerprint; concurrency Version |
| MiniAppOAuthClients | App FK unique; ClientKey globally unique; Confidential ClientType; created/updated/revoked timestamps |
| MiniAppClientSecrets | Client FK; AES-GCM ciphertext; external key version; created/expires/revoked/last-used; filtered unique index permits one non-revoked secret per client |
| MiniAppRedirectUris | App FK; exact URI bounded to 1024 chars; unique app+URI; timestamps |
| MiniAppAuditLogs | App/actor FKs; action; bounded metadata; app+created index; no credential values |
| MiniAppLogoUploads | Owner FK, temporary public ID/URL, optional attached App FK, expiry index; abandoned/replaced/deleted logos become cleanup candidates |

Categories are stable codes: utilities, shopping, education, services, entertainment, other; FE shows Vietnamese labels. No ANKT discovery filter added. Status is server-computed ConfigurationIncomplete or Draft. Future statuses are reserved in the enum; no request field grants review/publication privileges.

Generated PostgreSQL Up/Down SQL and model consistency were checked. Up only creates six tables/indexes; Down removes only these tables plus its EF migration-history entry. Down destroys Stage 3 data, so take a backup before rollback. No SQL was executed against the configured database.

## API contract

All routes require DeveloperActive authentication. Prefix `/api/developer/mini-apps`:

| Method / suffix | Behavior |
| --- | --- |
| GET / | Own apps, newest updated first, `?page=1`, 20/page; `{items,page,total,pageSize}`; no credentials |
| POST / | `{name,category,requestId,logoUploadId?}`; returns app DTO; requestId is a UUID kept across retries |
| GET /{appId} | Own app details; no credentials |
| PATCH /{appId} | Optional name/category/descriptions/website/support/policy/terms/logoUploadId; omitted properties unchanged, empty strings clear optional text |
| DELETE /{appId} | Soft delete, revoke client/active secret, schedule logo cleanup; 204 |
| POST /logo | Raw image/png, image/jpeg or image/webp bytes, maximum 2 MiB; `{uploadId,url}` |
| GET /{appId}/credentials | AppId, ClientKey, ClientSecret, ClientType, CreatedAt; authorized decryption and audit |
| POST /{appId}/credentials/rotate | Revoke old secret and create/decrypt replacement in one transaction; identifiers stable |
| GET /{appId}/redirect-uris | Array of exact URI strings |
| PUT /{appId}/redirect-uris | `{redirectUris:[...]}`; atomic replacement; max 20, deduplicated |
| GET /{appId}/activity | Latest 100 action/timestamp records |

Create transaction uses Serializable isolation for quota/idempotency checks and writes app/client/encrypted secret/audit together. Reusing an owner/requestId with changed input or a deleted app returns 409. Unique identifiers/indexes are database-enforced. Serialization/uniqueness/concurrency conflicts return 409 with retry guidance; retry creation with the same requestId. Default quota is 50 active apps/developer, configurable 1–500.

Sensitive create/upload/credential-read/rotation endpoints use a 20/minute account partition. Other operations reuse the existing Developer 180/minute account quota. Rate limiters are process-local like Stage 2; distributed quotas are needed if deploying multiple API replicas.

Callbacks require absolute HTTPS URLs without userinfo, wildcard or fragment. Development alone permits HTTP for localhost, 127.0.0.1 or ::1. Website/policy URLs use the same HTTPS policy; no server-side fetch of partner URLs occurs. Boundary validation bounds names, descriptions, email, counts and bodies. Existing `{error:{code,message,details?}}` conventions remain.

## Credential and storage decisions

- AppId, ClientKey and ClientSecret each use 32 cryptographically random bytes, encoded Base64Url with distinct prefixes.
- AES-256-GCM uses random 96-bit nonces, 128-bit tags and associated data bound to client UUID and key version. Ciphertext includes nonce/tag; the external encryption key is never stored in the database. This follows the [Microsoft AesGcm API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aesgcm.encrypt?view=net-8.0).
- Versioned keys are configured server-side via `MiniApps__ActiveKeyVersion` and `MiniApps__EncryptionKeys__<version>`. Missing/malformed keys or failed authentication tags fail closed with sanitized 503. Retain old versions and back them up securely while ciphertext references them. Changing the active key version affects newly created/rotated secrets; this is distinct from client-secret rotation.
- Credentials are absent from collection/detail/audit DTOs and initial HTML. BE responds no-store; BFF private,no-store. Browser keeps plaintext only in the mounted integration section, clears on tab exit, document hide, route blur or logout, and discards late async responses. No localStorage/sessionStorage/analytics/URL credential writes were added.
- Expo retains stacked screens; the Developer feature unmounts content when unfocused, preventing hidden screens from retaining credentials or duplicate forms.
- Real signed Cloudinary uploads reuse existing account configuration. Validate declared MIME, signature and size before upload; Cloudinary actually decodes and re-encodes a 512×512 PNG center crop. See [Cloudinary .NET upload](https://cloudinary.com/documentation/dotnet_integration) and [transformation reference](https://cloudinary.com/documentation/transformation_reference).
- Owner-bound upload tickets are persisted before storage I/O. Failed/abandoned uploads expire after one hour. The worker checks up to 100 expired unattached tickets every 15 minutes, deletes the external asset and then its ticket, retries failures with sanitized logging. Replaced/deleted app logos become eligible after one hour. Actual worker scheduling/long-running retry was not exercised for 15 minutes in this run.
- Vercel pagination preserves query parameters through the rewritten function URL. Binary requests support raw streams and platform-preparsed Buffers. [Vercel Node helpers source](https://github.com/vercel/vercel/blob/main/packages/node/src/serverless-functions/helpers.ts) confirms its parser restores unsupported MIME bodies to a readable stream; actual Vercel deployment remains unverified.

## Frontend routes and behavior

- `/developers/console` and `/developers/console/apps`: real own-app list, skeleton/empty/error/retry, pagination and clickable cards.
- Sidebar/dashboard Create Mini App opens the same native dialog in place, with focus containment, Escape/close/cancel, real logo upload/preview, validation, submitting state and retry-safe request ID.
- `/developers/console/apps/{appId}`: overview, information editor, integration/copy/confirmed rotation, callbacks, future sandbox notice, review checklist and recent activity.
- Drafts remain in form state on API failure; successful saves update the persisted DTO and dirty indicator. Internal clicked links and sidebar creation warn before discarding drafts.
- Detail entry intentionally uses document navigation. Installed Expo's web linker cannot roll back canceled SPA popstate accurately; native beforeunload provides reliable Back/Forward/reload warning without corrupting URL/history. All routes remain in the same FE and origin. No alert() was added.
- Desktop sidebar, mobile drawer, wrapping identifiers, full-width mobile form and responsive dialog. Client Secret help explicitly reserves it for partner backends; future frontend-only integrations require Public Client + PKCE.

## Files

BE new: `Viora.Domain/Developers/MiniAppEntities.cs`; `Viora.Application/Developers/MiniAppContracts.cs`; `Viora.Infrastructure/Developers/{MiniAppService,MiniAppSecretProtector,MiniAppLogoStorage}.cs`; `Persistence/Configurations/MiniAppConfigurations.cs`; migration and Designer; `viora-BE/Developer/MiniAppApi.cs`; `Viora.Application.Tests/{MiniAppInputTests,MiniAppServiceTests,MiniAppApiTests}.cs`; plan, this handoff and `docs/examples/mini-apps.env.example`.

BE modified: AppDbContext, model snapshot, DeveloperApi body limits, Program registration/mapping, disposable DeveloperE2EHost (ephemeral test key, explicit E2E storage environment and asset cleanup).

FE new: `features/developer/components/{mini-apps,mini-app-detail}.tsx`, `lib/mini-app-client.ts`, `mini-app.css`, `tests/mini-app.e2e.ts`, `server/developer/mini-app-proxy.test.cjs`, Stage 3 handoff.

FE modified: developer-screen.web.tsx, navigation.web.tsx, account.e2e.ts, server/developer/account-proxy.cjs, handler.cjs and handler.test.cjs. Replaced unused Stage 2 placeholder `pages/apps.tsx`. Unrelated existing dirty ANKT files were preserved. No new runtime dependency, commit, push, secret-file edit or deployment.

## Local run

1. Configure the existing local BE connection, Developer SMTP/BFF settings and existing Cloudinary account as in Stage 2. Add **server-only** MiniApps key/version settings from `docs/examples/mini-apps.env.example`; generate fresh random 32 bytes encoded as Base64. Never use an example/test key in an actual environment.
2. Review/apply migration only to an explicitly selected disposable/local database: `dotnet ef database update --project Viora.Infrastructure --startup-project viora-BE`. This command was not run against the configured database. Production still requires separate authorization and backup.
3. BE: `dotnet run --project viora-BE`. FE root: set ANKT_API_BASE_URL to that local API, DEVELOPER_SITE_ORIGIN to your FE origin; `npm run web` and open `/developers/console` using a verified Developer account.
4. Isolated E2E: run `dotnet run --project tools/DeveloperE2EHost`. It uses loopback5318, disposable SQLite, random in-memory encryption key and real MailKit SMTP on2526; never loads the production DB connection. Supply only `ANKT_E2E_Cloudinary__CloudName`, `ANKT_E2E_Cloudinary__ApiKey`, `ANKT_E2E_Cloudinary__ApiSecret` for real logo checks. Shut down gracefully to remove its test Cloudinary assets/database.
5. FE for E2E: ANKT_API_BASE_URL=http://127.0.0.1:5318, DEVELOPER_SITE_ORIGIN=http://localhost:8082, `npx expo start --web --port 8082`; set DEVELOPER_E2E_ORIGIN=http://localhost:8082 and run `npm run test:developer:e2e`. Test itself owns the actual loopback SMTP sink; do not run another sink on2526 simultaneously. Port8082 was chosen because8081 was already occupied.

## Verification and limits

- BE full suite: **107 passed, 1 existing database-dependent test skipped**. The 24 new cases comprise 16 pure unit/model checks and 8 SQLite/HTTP integration tests. Includes HTTP guest/ANKT-token rejection; real verified sessions; create/replay/validation; all owner-bound reads/writes/credentials/rotation/callback/activity/delete; forbidden status injection; DTO secrecy/no-store; encryption/tamper/wrong-client/missing-key; logo ticket ownership/reuse/expiry; quotas; persistence; database-trigger-induced creation/rotation rollback; callback policy; additive/reversible migration constraints.
- BE API build and integration-host build: passed, zero compiler warnings/errors. Existing model warning about ReportReason's default sentinel remains unrelated. One host rebuild initially failed because the running test host held DLLs; stopping that specific test process resolved it.
- Migration has no pending model changes; PostgreSQL Up/Down scripts generated and inspected. PostgreSQL migration execution and concurrent PostgreSQL serialization interleavings remain unverified; SQLite integration does not establish Npgsql concurrency behavior in production.
- FE Developer suite: **22 passed** (existing regressions plus API allowlist, unsafe path/method/CSRF rejection, binary preservation/size, JSON bounds and deployed rewrite pagination). Strict TypeScript and focused ESLint passed with zero warnings/errors. Root Expo production web export passed.
- Real SMTP → BFF → backend/database → Cloudinary E2E passed three times, including auth recovery regressions, real logo decode/upload, create/replay, credentials, draft/website/callback save/reload, rotation with stable identifiers and audit. Independent read-only SQLite assertions confirmed app/client/encrypted secrets, one active rotated secret, callback and attached logo. No mock API/storage/authentication was substituted. Final graceful shutdown removed its own Cloudinary asset and disposable database; earlier fixtures/assets were also cleaned up. The test-host cleanup initially accessed a disposed provider; using StartAsync/WaitForShutdownAsync and disposing after cleanup fixed it. The Expo test server on8082 was stopped; the preexisting8081 server was preserved.
- Chrome: guest Console protection, real login/session restoration, modal create and navigation, saved draft/reload, rejected HTTP URL preserves form, Copy Key/Secret and confirmed rotation, secret inputs removed on integration-tab exit, a single active editor DOM, custom dirty-create warning, native browser Back canceled with URL and draft preserved. No credential values were printed/captured in browser snapshots.
- Responsive inspection at375 and320 actual CSS pixels found no page/content horizontal overflow; desktop1440 inspected. Browser upload-file tool denied repository files due its configured workspace roots; browser file-picker upload remains unverified, while the same real upload endpoint/BFF/Cloudinary flow passed E2E. No claim of exhaustive mobile touch-device testing.
- Expected dev-only anonymous401 resources and existing RN Web/meta deprecation warnings were observed. No new application exception was observed in the verified flow. No external telemetry was added.
- Before production: persistent managed keys/backups, approved migration, real deployed HTTPS/BFF/Cloudinary checks, long-running cleanup/retry and multi-replica quota policy. Existing SPA public SEO limitations remain as documented in integrated-FE handoff.

## Stage 4 dependencies

Real OAuth/OIDC authorization/token server, client authentication using active non-revoked secrets, Public Client + PKCE, exact callback matching, domain verification, sandbox runtime, review/admin authorization and publication transitions. LastUsedAt/expires/revoked/client-type/status fields prepare these boundaries; none of those Stage 4 systems are simulated or enabled here.
