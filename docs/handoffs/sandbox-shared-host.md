# Sandbox on the existing Production-mode SSO host — 26/10/2026

## Outcome

The existing Render backend can host Sandbox Auto Login while remaining in
`ASPNETCORE_ENVIRONMENT=Production`. No second backend, issuer, hostname or
certificate set is required. This supersedes the environment-wide deny in the
original sandbox-auto-login handoff.

SandboxTest:Enabled defaults to false; explicit true enables the subsystem on
any host. On Production, issuer and every allowed Portal origin must be HTTPS.
Owner/DeveloperSession, Sandbox OAuth client, audience, code/PKCE, scope,
client-version and expiring/revocable session checks remain in force. Production
JWT APIs reject Sandbox tokens; normal user JWTs still work on Production APIs.

## Changes

- `Viora.Infrastructure/Developers/SandboxTestOptions.cs`: remove the unconditional
  Production override; validate HTTPS configuration for explicitly enabled hosts.
- `Viora.Application.Tests/SandboxTestIdentityTests.cs`: run real code/UserInfo,
  reopen/revoke, expiry and Production API isolation in both Testing and Production.
  Test default-off, explicit disable and insecure Portal origin rejection.
- `tools/SsoE2EHost/SandboxIdentityBrowserFixture.cs`: Chrome fixture now uses an
  actual Production-mode host with isolated local PostgreSQL and HTTPS.
- Local ignored `.env`: set only SandboxTest__Enabled=true,
  SandboxTest__PortalOrigin=https://mxh.ankt.vn, SandboxTest__SessionMinutes=15.
  The edit verified that all other configuration values remained unchanged.
- `.env.example`, runbook, spec and ADR: document same-host opt-in and HTTPS rules.
  Previous unrelated working-tree changes were preserved.

No additional migration, dependency, endpoint or token mechanism was introduced.
The two identity migrations from the previous feature are still prerequisites.

## Verification

- RED before fix: 4 failed / 14 passed, proving the Production deny and missing
  HTTPS option policy. Artifact: sandbox-shared-host-red.trx.
- Final Sandbox + SSO regression: **199 passed / 0 failed / 0 skipped**.
  Includes SQLite and disposable PostgreSQL, real OpenIddict code + PKCE and
  signed tokens. Artifact: sandbox-shared-host-final.trx.
- BE/E2E host build: **0 warnings / 0 errors**. The test project separately emits
  the existing TrustedProxyHeadersTests xUnit analyzer warning.
- Chrome E2E: **Passed** in Production mode. Two Developer contexts launch
  concurrently, receive the same database User but different pairwise subjects
  and partner sessions; zero consent prompts, reopening succeeds, cross-owner
  access returns 404, mobile 375px has no horizontal overflow.
- Database evidence confirms Production-mode host, one shared Sandbox account,
  two mappings and three sessions reaching UserInfo. The cluster was disposable
  and removed at shutdown; no configured application DB was contacted.
- Local .env remains Git-ignored. Whitespace checks pass. No secrets were added
  to source/docs or printed by the configuration edit.

Artifacts under `.codex-tmp/`: sandbox-shared-host-{red,final,browser,build}.log,
`test-results/sandbox-auto/sandbox-shared-host-*.trx`,
`sandbox-auto-database.json`, `sandbox-identity-mobile.png`.
The full backend suite was not rerun for this follow-up; the prior full-suite
proxy-log failures are documented in the original handoff. The affected Sandbox
and SSO suites were run on the final implementation.

## Render rollout still required

Local changes do not update Render Environment. Keep the existing Production
environment, SSO issuer, first-party origin and certificate configuration.
Confirm/apply the existing `AddSandboxTestIdentities` and
`ShareSandboxTestAccount` migrations through the approved schema-change process,
deploy the updated backend and matching Portal, then enable:

```dotenv
SandboxTest__Enabled=true
SandboxTest__PortalOrigin=https://mxh.ankt.vn
SandboxTest__SessionMinutes=15
```

Mini App Sandbox client, exact HTTPS callback and verified domain must be ready.
Disable the flag to stop new launches and reject existing Sandbox access through
the normal session checks after configuration reload/restart. No deployment,
live database migration, remote configuration update or commit was performed in
this follow-up. See [current runbook](../operations/sandbox-auto-login.md).
