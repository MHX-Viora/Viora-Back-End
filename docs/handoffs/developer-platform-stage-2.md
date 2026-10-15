# ANKT Developer Authentication — Stage 2

## Architecture and scope
Independent Developer identities in the existing AppDbContext/database. Existing ANKT Accounts/Users/JWT flows are unchanged. The portal remains on `https://mxh.ankt.vn/developers`, per the user's domain decision. No new backend/database, no Mini App APIs or Stage 3 functionality.

Developer APIs use the dedicated `DeveloperSession` authentication scheme and `DeveloperActive` policy. ANKT Bearer JWTs cannot authorize them. Opaque 256-bit session identifiers are hashed in the database; the Next BFF alone receives the raw credential and places it in an HttpOnly host-only cookie. No Developer role implies ANKT Admin privileges.

## Schema and migration
Configured database inspected on 2026-10-15 in an explicit READ ONLY transaction: Accounts, Users, RefreshTokens and ExternalLogins exist; zero Developer-prefixed tables. Existing ANKT account/session tables couple social roles/profile semantics and are not reused for independent credentials.

Migration: `20261015074801_AddIndependentDeveloperAccounts`.

| Table | Purpose |
| --- | --- |
| DeveloperAccounts | Name, unique normalized email, Identity password hash, verification/status, terms timestamp/version, lockout and security version |
| DeveloperSessions | Hashed opaque credential, account FK, device info, idle/absolute expiry, revocation |
| DeveloperActionTokens | Shared purpose-bound verification/reset tokens, hashes, expiry, consumption and account security version |
| DeveloperExternalIdentities | Unique provider+subject and account FK; reserved for a future real federation provider |
| DeveloperAuditLogs | Account FK, action, attributed IP, bounded user agent and non-secret metadata |

Up adds only these five tables and indexes; Down removes only these five tables. Account deletion cascades its sessions/action tokens/identities and sets audit account FK null. No existing ANKT table, row or migration is deleted or rewritten. Model snapshot and generated PostgreSQL DDL reviewed; no pending model changes. Migration was **not applied** to the configured database. Production application requires explicit authorization and backup; rollback removes new Developer data, so back it up first.

## API contract
All paths below start with `/api/developer`; `/health` remains the Stage 1 liveness endpoint.

| Method/path | Access and behavior |
| --- | --- |
| POST /auth/register | Public; validates consent, name, email/password/confirmation, creates pending account, neutral 202 |
| POST /auth/login | Public; generic failures, active verified users only; credential returned only to the BFF |
| POST /auth/verify-email | Public; single-use hashed 24-hour token; no auto-login |
| POST /auth/resend-verification | Public; neutral 202 and 60-second per-account/purpose cooldown |
| POST /auth/forgot-password | Public; neutral 202, no account-existence response |
| POST /auth/reset-password | Public; 30-minute token; password replacement invalidates action tokens and revokes sessions |
| GET /auth/me, GET /profile | DeveloperActive; minimal DTO, never entity/password/token hashes |
| POST /auth/refresh | DeveloperActive; renews idle expiry within hard bound, no access/refresh JWT pair |
| POST /auth/logout, POST /auth/logout-all | DeveloperActive; server-side revocation |
| POST /auth/change-password | DeveloperActive plus current-password reauthentication; revokes all sessions |
| PATCH /profile | DeveloperActive; name and optional HTTPS avatar URL only, email unchanged |
| GET /sessions | DeveloperActive; own active sessions only |
| DELETE /sessions/{id} | DeveloperActive; ownership-bound, idempotent revocation |
| GET /linked-accounts | DeveloperActive; provider and link time, no provider subject exposure |
| DELETE /linked-accounts/{id} | DeveloperActive plus current password; cannot remove the last login method; revokes sessions |
| POST /linked-accounts/ankt/start, GET /linked-accounts/ankt/callback | 501 ANKT_FEDERATION_UNAVAILABLE, no auth simulation or linking side effects |

Known errors use `{error:{code,message,details?}}` and appropriate 400/401/403/404/409/413/429/501/503 status. Existing global handler retains sanitized unexpected 500/traceId behavior. All Developer responses are no-store, bounded JSON request bodies (16 KiB). Endpoint annotations and service validation both apply.

## Security decisions
- ASP.NET Identity PBKDF2 PasswordHasher with 210,000 iterations; password length 12–128 and basic diversity, never plaintext/hash responses.
- Email/reset/session values use cryptographic random 32 bytes and SHA256 storage hashes. Verification/reset tokens are consumed atomically in the same transaction as account changes, with expiry/purpose/security-version checks.
- Password replacement uses atomic security-version and status checks. Password change also rechecks the persisted lockout in the write predicate, preventing an interleaved lock from being cleared. Recovery with email proof can reset a locked account.
- Login, change-password and unlink share persistent account lockout after five failures for 15 minutes. Login uses a dummy hash for absent/passwordless accounts. Active verified status is checked on every session authentication.
- Idle session expiry is 8 hours. Hard lifetime is 8 hours, or 30 days for remember-me; refresh cannot exceed the hard bound. Logout/revocation is database-backed. LastUsedAt records refresh activity.
- Public quota: 60/minute per client IP; protected quota: 180/minute per account. Production BFF signs IP/time/method/path with a shared server-only secret. Nginx overwrites the client IP header; malformed/mismatched proofs fail closed. Direct clients without proof fall back to peer IP. Shared signature grants no account authority.
- SMTP uses MailKit STARTTLS or implicit TLS, normal certificate validation. Insecure loopback sinks are permitted only in Development with an explicit flag. Tokens are committed before sending; delivery failure logs contain only exception category, not SMTP text/credentials/recipient/token. Users can resend after cooldown.

## Files and configuration
- Domain: `Viora.Domain/Developers/DeveloperEntities.cs`.
- Application DTOs/contracts/validation: `Viora.Application/Developers/DeveloperContracts.cs`.
- Infrastructure: `Viora.Infrastructure/Developers/`, persistence configurations, DbContext and migration/snapshot. MailKit 4.18.1 added; its transitive System.Security.Cryptography.Pkcs pinned to patched 10.0.13 following NuGet audit ([Microsoft advisory](https://github.com/advisories/GHSA-846f-f764-76mx)).
- API: `viora-BE/Developer/DeveloperApi.cs`, `DeveloperAuthentication.cs`, `DeveloperIngress.cs`; three isolated Program registrations/middleware/mapping changes.
- Tests: DeveloperIdentityTests, DeveloperApiTests, DeveloperBoundaryTests. Loopback-only real integration host: `tools/DeveloperE2EHost`.
- Environment example: `docs/examples/developer-auth.env.example`. Set PortalUrl, a matching BffSharedSecret, SMTP host/port/from/user/password/TLS options. No secrets or production environment files modified.

## Verification and limits
- Full BE suite: 83 passed, 1 existing database audit skipped. Auth/API subset rerun after ingress fixes; signed clients have separate anonymous buckets and authenticated reads remain available after an anonymous bucket is exhausted.
- NuGet vulnerability audit after pinning patched PKCS: zero vulnerable packages. Full BE suite and real SMTP/BFF E2E passed again with the patched dependency.
- Deterministic SQL interleaving regression protects password-change lockout. Tests cover duplicate/invalid/weak registration, verification expiry/use/purpose, pending/suspended users, lockout/current-password failures, session expiry/refresh/logout/revocation/ownership, password reset/change, last login method, migration boundaries and rate limits.
- Real end-to-end flow through Next BFF + real services + isolated SQLite + actual MailKit SMTP succeeded. No mock authentication or public test-token endpoint. PostgreSQL DDL/model checks passed; production migration execution remains unverified by design.
- ANKT federation is not implemented: no OAuth/OIDC provider exists. Continue-with-ANKT is disabled, not a fallback accepting social JWTs. Stage 3/federation needs a real provider, client registration, PKCE/state/nonce and ownership-confirmed linking contract.
- Production still needs approved migration application, real SMTP delivery/domain configuration, matching BFF keys/trusted ingress and HTTPS cookie verification. Review account terms/support/data retention before enabling public registration. Synchronous email delivery has timing/retry limits; a durable encrypted outbox is an extension for stronger guarantees at scale.
- No commit, push, production migration, deployment, secrets change or database reset was performed for Stage 2.
