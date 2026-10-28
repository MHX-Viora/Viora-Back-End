# Database-backed Mini App partner credentials

## Objective / contract
Every existing/new Mini App can obtain independent Sandbox and Production secrets
without app-specific runtime env entries. Existing Client IDs and partner login
endpoints stay stable. Owner reveal lazily provisions a 256-bit random secret;
repeat reveal returns the same value. The existing versioned AES-GCM protector
encrypts recoverable values; SHA-256 hashes validate random bearer secrets.

An owner must have a live, active Developer session and own a non-deleted app.
Reveal, rotate and revoke are POST-only, same-origin BFF routes with no-store,
existing sensitive-operation rate limits and secret-free audit metadata.
Rotate/revoke require an optimistic credential version. Revoked credentials stay
revoked until explicit rotation; no env fallback may resurrect them.
Credentials never appear in public app DTOs, logs, or EXPO_PUBLIC build settings.

## Structure / ordered plan
1. Add credential entity + configuration and focused SQLite tests. Keep conventions:
   scoped services, explicit domain errors, existing `MiniAppSecretProtector`.
2. Implement provision/reveal/rotate/revoke and DB-only partner validation. Generate
   migration and verify it offline; never apply to a real database during this task.
3. Add explicit, transactional, idempotent env import command. Validate legacy
   hashes/secret pairs, reject unknown/ambiguous clients and never overwrite DB rows.
   Hash-only legacy credentials remain valid but require rotation to reveal a value.
4. Remove app-specific FE credentials; extend authenticated BFF/UI and browser tests.
5. Run focused BE login/owner/credential tests, FE TypeScript/lint/BFF/browser checks;
   review authorization, concurrent provisioning and stale rotation, update handoff.

## Commands / boundaries
BE: `dotnet test Viora.Application.Tests --no-restore -m:1 /nodeReuse:false /p:UseSharedCompilation=false --filter "FullyQualifiedName~MiniAppPartner|FullyQualifiedName~MiniAppCredential|FullyQualifiedName~MiniAppAuthBridge"`.
FE: `npx tsc --noEmit`; `node --test server/developer/auth-bridge-policy.test.cjs`;
`node features/developer/tests/client-secret.browser.mjs`; changed-file ESLint.
Migration: `dotnet ef migrations add AddMiniAppPartnerCredentials --project Viora.Infrastructure --startup-project viora-BE` with a synthetic design connection.
No deployments, live imports, live migrations, new dependencies or secret rotation
in production. Preserve unrelated uncommitted proxy diagnostics and toolbar changes.
