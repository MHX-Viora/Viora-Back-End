# ANKT Developer platform upgrade

## Objective and assumptions
Implement the attached October 13 brief in existing .NET/AppDbContext,
React/Vite Developer/admin portal and Expo host. Keep existing account identities,
published configurations and public OAuth endpoints. Preserve earlier uncommitted
changes. No deployment or production migration is implied; generate and test safe
migrations first. Existing Developer entities represent organizations/profiles;
extend them instead of duplicating tables.

## Contracts and ordered slices
1. Audit existing auth, lifecycle, membership and OAuth tests.
2. Authentication: reuse account registration/login/Google/Firebase verification.
   Extend DeveloperReauthInput with optional FirebaseToken, verify signed identity,
   existing external subject and recent auth_time; password remains supported.
3. Credentials: keep existing ClientSecretHash as current credential. Add one
   previous hash and bounded grace expiry to MiniApp (no plaintext). Existing
   rotate POST remains immediate by default; optional graceMinutes 0..1440.
   Add metadata GET /credentials and reauthenticated POST /revoke-previous-secret.
   OAuth and all confidential-client verifiers accept previous only within grace.
4. Domains: existing challenges keep legacy root filename. New challenges use
   random root filename, token and expiry; extend DTO with verificationFileName,
   verificationUrl, expiresAt, fileContent, lastCheckedAt, lastError. Add renew
   challenge route. Preserve existing verified rows and secure pinned-DNS HTTPS
   transport. Distinguish HTTP/content/expiry/network failures. Download/copy needs
   app permission/session, never password.
5. Membership: reuse DeveloperMembership for organizations, introduce app-scoped
   membership only where required. Explicit organization selection and per-app
   access/capability checks; no automatic email-only linking; owner transitions
   must check privileged proof and preserve an owner. Invitation acceptance binds
   authenticated verified identity. Avoid ambiguous SingleOrDefault membership.
6. Portal: brand teal tokens, responsive sidebar, eight app sections (overview,
   connection, domain, SSO, members, releases, analytics, settings). Copy controls
   below identifiers/URLs; one-time secret disclosure; list search/filter/sort/page.
   Approved Developer shows status/change-request workflow instead of new submit.
   Public documentation remains public. Admin retains separate review/publication.
7. Additive migrations, integration tests, browser validation, review and handoff.

## Structure and conventions
Viora.Application/MiniApps: additive DTOs/interfaces. Viora.Infrastructure/MiniApps:
services; Persistence/Configurations and Migrations: PostgreSQL schema.
Viora.Application.Tests: xUnit/SQLite/HTTP/security regression. Portal source:
viora-admin/src/features/developer; host: viora/features/developers. Existing
service patterns, explicit MiniAppException codes and capability checks apply.

Example: `throw new MiniAppException("PERMISSION_DENIED", "Không có quyền.", 403);`

## Verification commands
BE: dotnet test Viora.Application.Tests --no-restore -m:1 -nr:false -p:UseSharedCompilation=false
EF: dotnet ef migrations add UpgradeDeveloperPlatform --context AppDbContext --project Viora.Infrastructure --startup-project viora-BE
Portal: npm run build; npm run lint; node --test tests/*.test.mjs
Host: npx tsc --noEmit; npm run test:developers; npx eslint features/developers

## Boundaries and acceptance
Authenticated role enforcement; passwordless Google reauth never asks for invented
passwords. No secret GET/plaintext persistence/logging/storage. Cryptographic
challenges, exact host/callback checks, SSRF/DNS rebinding protection. Legacy data
continues to function. Tests cover new/old accounts, identity conflicts, scoped
permissions, credential grace/revocation, challenges/expiry/errors, publish gates,
copy/actions and responsive states. Report unavailable environment capabilities
honestly; do not invent MFA, metrics, provider configuration or successful tests.
