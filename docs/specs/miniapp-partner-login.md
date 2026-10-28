# Mini App partner login

## Objective and contract
Extend the existing profile Auth Bridge with `auth.getLoginCode()`. The SDK obtains
a 60-second one-use code from its authenticated, origin-bound Bridge session.
The partner backend exchanges it using registered app credentials and creates its
own session, keyed by `(miniAppId, environment, user.id)`. No ANKT JWT or partner
secret enters the frame. Keep the current PKCE exchange and ANKT login intact.

Use existing MiniAppBridgeSessions: partner grants store the parent Bridge session
UUID in CodeChallenge (36 characters); PKCE challenges are always 43 characters.
Neither exchange can consume the other protocol's code. Store only code hashes.
Recheck parent/source session, user, domain and publication on exchange/validation.
Partner credentials are app-specific SHA-256 hashes in backend configuration,
with independent Sandbox/Production secrets. No OIDC store is used.

## Structure, style and boundaries
Backend records in Viora.Application/Developers; service in Infrastructure/Developers;
routes in Developer/MiniAppAuthBridgeApi.cs. SDK in packages/miniapp-sdk/src/host-auth.ts.
Follow typed records, EF atomic updates, DeveloperAuthException and existing tests.
Example: `throw new DeveloperAuthException(401, "INVALID_CLIENT", "Invalid partner credentials.");`
No unrelated modules, existing login changes, dependencies or database migration.
No live deployment or external partner repository is assumed available.

## Ordered tasks and verification
1. Add HTTP integration tests for issuance, authenticated exchange, replay, expiry,
   app/secret/environment substitution, source logout and session validation.
   Verify: `dotnet test Viora.Application.Tests --filter FullyQualifiedName~MiniAppPartnerLoginTests`.
2. Implement code grants, partner credentials, atomic consumption and validation.
   Verify focused tests plus existing MiniAppAuthBridgeTests/MiniAppBridgeRevocationTests.
3. Add SDK getLoginCode using existing iframe/native transports and cancellation.
   Test Web iframe, Android window messages, iOS document messages, retry/reopen.
   Verify: `npm --prefix packages/miniapp-sdk test`; `npx tsc --noEmit`.
4. Provide partner TypeScript example, configuration, Render/device checklist and
   handoff with actual test results. Review protocol separation and secret handling.

## Acceptance and limits
Expiry <=60s; one atomic exchange winner; registered app and verified environment;
no wildcard messaging; no partner secrets in frontend; no infinite retries.
Partner protected endpoints must validate the ANKT grant before reusing their
own session, including after account switch/logout. Native device verification and
Nghệ Thuật Số deployment require the actual runtime/repository; report gaps honestly.
