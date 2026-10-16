# Stage 4: Integration configuration and domain verification

## Objective / authorized boundaries
Implement the supplied Stage 4 requirements on existing .NET8/EF8 DeveloperSession, shared AppDbContext and integrated Expo portal `/developers`. Stage3 BE commit is `2d9819a9`; FE is still uncommitted and must be preserved. No production migration, deployment, token endpoint, SDK, sandbox runtime or admin review. User confirmed no real HTTPS test domain is available; use controlled tests and explicitly leave public-domain E2E unverified.

## Decisions and contract
- Reuse MiniApp fields/callbacks/audit/auth/storage. Add SsoEnabled configuration flag only (does not implement SSO).
- Add MiniAppDomainVerifications with historical challenges, UUID/file identifier, canonical hostname, status/timestamps, random32-byte token hash and AES-GCM encrypted material using existing external versioned keys and a separate cryptographic purpose. Repeat download returns the same active challenge. Challenge7-day lifetime, successful proof30-day lifetime; expiry/recheck must be reflected honestly.
- Independent proof per app. A hostname is not globally reserved: serving each unique app-bound challenge authorizes that app; another account cannot reuse another app's file/token. No domain ownership inferred by matching account/email/hostname alone.
- Host change or clearing website revokes previous challenge in the same update transaction, including the existing PATCH endpoint. Same canonical host/path-only changes keep proof. Regenerate revokes old challenge; history remains. Soft delete revokes proof. Concurrency Version protects network result commit after config/change/regenerate/delete.
- Website/policy normalization: HTTPS, valid canonical IDN DNS hostname, no credentials/fragment/backslash/control/IP literals/special-use host, port443. Callbacks preserve existing exact-match URI semantics with Development loopback exception; no OAuth behavior added.
- Safe fetcher: fresh DNS resolution per operation, reject every non-public IPv4/IPv6 answer (including mapped/transitional/reserved forms), pin socket connection to checked IP via SocketsHttpHandler.ConnectCallback. Original DNS URL remains for SNI and normal certificate validation. Proxy/cookies/credentials/redirects/compression disabled; HTTP1.1 exact, timeout8s, capped headers/body, bounded reads. No unsafe production bypass for tests.
- Verification endpoint only fetches fixed HTTPS host plus generated root filename. Match full expected format/AppId/host and compare token hashes constant-time. No logs or audit metadata contain verification tokens.
- Readiness performs bounded safe policy checks on the stored URLs. Required logo/name/category/descriptions/site/domain/policies/support; callbacks required only when SsoEnabled. Return passed/failed/warning and required/optional severities, configuredReady separately from runtime availability. readyForSandbox/readyForReview always false until future systems exist.
- BFF must pass only safe attachment content/filename for owner-authorized .txt downloads, never assume all upstream responses are JSON. Mutation CSRF and no-store stay intact.

## Ordered slices / acceptance / verification
1. URL/IP policy + safe fetcher tests: reject malicious/private/reserved/mixed DNS, redirects, oversized/slow/wrong-media/TLS failures; controlled real TLS shows connection pinning and certificate verification.
2. Entity/configuration/contracts: additive reversible migration, one active challenge/app, restrictive FK, no model drift.
3. Domain lifecycle and APIs: actual DB transactions, authenticated ownership on all endpoints, repeatable file/no secret, valid/invalid proof, host-change invalidation across old/new write APIs, concurrency race regression.
4. Integration/readiness: atomic saved configuration, callback validation, public policy probes, no-SSO callback optional, truthful readiness/runtime states.
5. BFF/frontend: integration tab loads real config, saves drafts, handles download/verify/regenerate, shows statuses/errors/checklist, protects drafts and mobile layout. Preserve Stage3 credentials lifecycle.
6. Verify: full BE tests/build, FE tests/typecheck/lint/build, local SMTP/BFF controlled integration and browser checks. Fresh security review and concise handoff with explicit remaining E2E limits.

## Commands / structure / conventions
Existing project folders remain: Domain/Developers entities, Application/Developers DTOs/validation, Infrastructure/Developers services/network code, API Developer endpoint mapping, Application.Tests fixtures; FE features/developer and server/developer BFF.
`dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
`dotnet build viora-BE/viora-BE.csproj --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false`
Generate only: `dotnet ef migrations add AddMiniAppDomainVerification --project Viora.Infrastructure --startup-project viora-BE --no-build` with synthetic design connection.
FE `npm run test:developer`, `npx tsc --noEmit`, focused `npx eslint`, `npm run build:web`.
Follow existing records/DTOs, DeveloperAuthException structured errors, explicit claim-based owners and UTC. Always validate/test; ask before production data operations; never commit secrets or weaken certificate/IP validation to make tests pass.

## Completion
All six slices implemented and reviewed. Backend 186 passed/1 existing skip; frontend 25 passed; TypeScript, focused lint and builds passed. Controlled real HTTPS E2E and browser error/status/dirty/mobile checks passed. Public Internet domain E2E and PostgreSQL migration execution remain unverified. See `docs/handoffs/developer-platform-stage-4.md` for complete evidence, operation notes and Stage 5 gate. No production changes.
