# ANKT Developer Platform — Stage 4

Implemented 2026-10-16. Integration configuration and app-bound domain proof only. Existing verified DeveloperSession, shared AppDbContext and integrated Expo `/developers` portal are reused. No production migration/deployment or Stage 5–7 implementation. Stage 3 backend commit: `2d9819a9`.

## 1. Entities and lifecycle

MiniApp adds `SsoEnabled` (configuration intent only). New `MiniAppDomainVerification` stores app/hostname, generated filename, token hash, purpose-bound encrypted token material, status, timestamps, expiry, revocation and concurrency Version. Restrictive FK, unique filename and filtered unique active proof per app retain historical challenges.

Pending challenges expire after 7 days; successful proof expires after 30 days. Repeated download returns the same active file. Regenerate revokes the previous challenge. Website hostname change/clearing through both integration PUT and legacy PATCH revokes proof atomically; canonical same-host path changes preserve it. Soft delete revokes it. Failed recheck immediately removes Verified status; historical VerifiedAt is not authorization. Concurrent regeneration, hostname changes and deletion invalidate in-flight verification results through Version comparisons.

Proof is per app, not exclusive global hostname ownership. Each app must serve its own generated file containing its AppId, canonical host and random token. No ownership transfer is inferred from account email or matching hostname. Token hashes are compared in constant time; file contents contain no Client Secret. Tokens are excluded from DTOs, audit metadata and logs, except the explicitly requested owner-authorized proof download.

## 2. Migration

`20261016022554_AddMiniAppDomainVerification`: adds SsoEnabled with default false, proof table and indexes. Up/Down SQL and EF model consistency were checked. Down drops only the new table and column and destroys proof history. No migration was executed against production. Check deployed migration history and backup before applying or rolling back; PostgreSQL execution remains unverified in this environment.

## 3. API

All routes below use `/api/developer/mini-apps/{appId}`, require active Developer authentication and filter ownership/deletion. ANKT social tokens cannot authorize them. BFF mirrors exact routes under `/developers/api/account/mini-apps/{appId}` with no-store and existing CSRF/session protections.

| Method | Suffix | Result |
| --- | --- | --- |
| GET / PUT | `/integration` | Load/save website, support email, descriptions, policy URLs, SSO intent and exact callbacks atomically |
| GET | `/domain-verification` | Effective status, host, file URL and timestamps; no token |
| GET | `/domain-verification/file` | Bounded plain-text attachment with generated safe filename |
| POST | `/domain-verification/verify` | Fetch only persisted host/generated root filename; persist outcome |
| POST | `/domain-verification/regenerate` | Revoke old challenge and create a fresh file |
| GET | `/readiness` | Saved metadata, domain and public policy checks |

Download/verify/regenerate/readiness share 10 requests/minute per developer; existing general limits remain. Limits are process-local, matching the existing architecture. Verification failures return structured domain status/error codes; client payload cannot set hostname, VerifiedAt/status or a fetch URL.

## 4. Frontend

New Mini App detail tab **Cấu hình tích hợp** includes saved configuration, SSO-conditional callbacks, domain file instructions/download/verify/regenerate confirmation, status/expiry/errors and readiness checklist. Dirty-state navigation confirmation, busy guards and stale-response protection preserve edits. Successful PUT merges returned metadata into the parent editor immediately. Failed refresh clears old domain/readiness results. Existing credential controls remain separate. Auth introduction now reflects available Mini App management.

`configurationReady` means configuration requirements passed. `readyForSandbox`, `readyForReview`, sandbox/review availability remain false. No OAuth authorization/token endpoint, SDK, actual SSO, sandbox execution, publication or review submission is added.

## 5. Changed files

Backend: Domain/Developers/MiniAppEntities; Application/Developers/MiniAppIntegrationContracts; Infrastructure/Developers/{MiniAppIntegrationService,MiniAppDomainLifecycle,PublicWebFetcher,MiniAppService,MiniAppSecretProtector}; AppDbContext, MiniAppDomainConfiguration, migration/designer/snapshot, Properties/AssemblyInfo; API Developer/{MiniAppIntegrationApi,MiniAppApi}; tests/{MiniAppIntegrationTests,PublicWebAddressTests,PublicWebFetcherTests,MiniAppApiTests}; tools/DeveloperE2EHost/{Program,ControlledDomainSite}; plan and this handoff.

Frontend: features/developer/components/{mini-app-configuration,mini-app-detail,auth-form}; lib/mini-app-integration; mini-app.css; tests/{domain-verification.e2e,mini-app.e2e,account.e2e}; server/developer/{account-proxy,handler,domain-proxy.test}; frontend handoff. Earlier unrelated frontend changes are preserved.

## 6. Environment

No new production secret setting. Reuse `MiniApps__ActiveKeyVersion` and `MiniApps__EncryptionKeys__<version>` (32-byte base64 AES key), with distinct `mini-app-domain` purpose. Retain old keys while encrypted challenges use them. Existing Developer auth, trusted BFF origin/attribution and storage settings remain required. Never copy test keys into production.

Public fetch needs outbound DNS and direct HTTPS 443; proxies are intentionally disabled. Standard system trust and hostname validation remain enabled. Online certificate downloads and revocation network access are disabled to prevent certificate-chain SSRF; remote servers must provide a complete trusted chain. This trades online revocation checking for bounded network behavior. Deployment-level egress controls provide additional defense.

## 7. Local execution

Normal local services use existing backend/Expo setup and `.env.example`; do not apply migrations to a configured production connection. For isolated E2E, build/run `tools/DeveloperE2EHost`, which creates disposable SQLite and ephemeral keys. Supply only test-owned Cloudinary credentials through `ANKT_E2E_Cloudinary__*` process variables. Set `ANKT_E2E_DOMAIN_FIXTURE=1` on that test host; it starts a private-CA loopback HTTPS website and supplies test-only DNS/socket routing. No production bypass or system trust-store modification is involved.

Start Expo/BFF with `ANKT_API_BASE_URL=http://127.0.0.1:5318`, `DEVELOPER_SITE_ORIGIN=http://localhost:8082`, port 8082. Run `DEVELOPER_E2E_ORIGIN=http://localhost:8082`, `DEVELOPER_DOMAIN_E2E=1`, `npm run test:developer:e2e`. The script owns SMTP port2526. Host shutdown removes its Cloudinary assets, SQLite and TLS fixture. AIA regression test additionally requires Python with ssl/OpenSSL in PATH to avoid Windows Schannel server-side certificate retrieval contaminating the client test.

## 8–11. Verification results

- Backend API and isolated integration-host builds passed with zero warnings/errors. Full backend suite: **186 passed, 1 existing database-dependent skip**.
- Frontend developer suite: **25 passed**. TypeScript, focused ESLint and production web export passed.
- Unit/network tests cover URL/IP policy, mixed DNS/private/reserved rejection, fixed checked-address socket, DNS deadline, status/media/empty/size/slow failures, invalid certificate/name, redirect refusal and certificate AIA trap.
- SQLite/API integration covers real sessions and ownership, attachment headers, repeat download, invalid token/AppId/host, failed rechecks, expiry during fetch, both website write paths, regeneration/delete races, active-proof uniqueness and readiness snapshot changes. Deterministic doubles cover failure/race cases; successful controlled TLS tests exercise real sockets.
- Full BFF/API/database E2E passed registration/SMTP/auth regressions, persisted configuration/callbacks, download, real file installation at HTTPS root, direct TLS fetch, verification/reload, hostname invalidation and readiness. Existing logo upload uses real test-owned Cloudinary storage.
- Browser checks confirmed loaded real configuration, save/reload persistence, missing-file error, successful verification/checklist, dirty navigation confirmation and 320px layout without horizontal overflow.

## 12. SSRF controls and limits

Only canonical DNS HTTPS URLs on port443; no userinfo/fragments/backslashes/control characters/IP literals/special-use hostnames. Every resolved address must be public; mixed answers are rejected. Checked IPs are pinned to the socket while original host supplies TLS SNI/name validation. No second DNS lookup, redirects, proxy, cookies, credentials, decompression or certificate-chain fetch. Total 8s deadline includes DNS/connect/headers/body; 16KiB headers, 4KiB proof and 256KiB policy bounds include streamed reads. Policies may use separate public domains; text/html/xhtml/pdf media are bounded, not browser-executed. Certificate AIA HTTP trap received no request. Conservative special-range denial may reject unusual legitimate networks.

## 13. Remaining verification limits

User confirmed no real public HTTPS test domain is available. **Public Internet domain E2E remains unverified**; controlled HTTPS results must not be represented as public-domain verification. PostgreSQL migration execution and provider-specific concurrent interleavings were not exercised; generated PostgreSQL DDL plus SQLite uniqueness/Version race tests passed. No unresolved failure remains in executed checks. No production database, migration, DNS, deployment or user-owned dev server was changed.

## 14. Gate for Stage 5

Review/apply migration through the deployment process, provision existing encryption keys and egress rules, run public-domain E2E and provider-level PostgreSQL verification, then separately authorize/specify Stage 5 OAuth/SSO contracts. Configuration readiness alone grants no runtime authorization. Stop at Stage 4.
