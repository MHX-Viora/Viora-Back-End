# Sandbox on the existing SSO host

Status: implemented; 199 Sandbox/SSO tests passed, Production-mode concurrent
Chrome E2E passed and E2E host build passed. Local .env updated; live rollout
remains pending. See ../handoffs/sandbox-shared-host.md.

## Objective and authorization
The user authorizes Sandbox Auto Login on the existing Render backend and Portal,
while the ASP.NET environment remains Production. Supersedes the environment-wide
disable in sandbox-auto-login.md. No deployment or live database mutation is part
of this implementation. Existing SSO issuer/certificates stay in use.

## Contract and boundaries
- SandboxTest:Enabled remains false by default; an explicit true enables the
  Sandbox subsystem on any host, including Production.
- Production hosts require HTTPS issuer and all permitted Portal origins.
- DeveloperSession ownership, Sandbox client environment, PKCE, scopes, audience,
  short-lived session, version/revocation and separate cookies remain mandatory.
- Sandbox tokens cannot authenticate Production JWT APIs; production account
  credentials cannot impersonate a Sandbox account.
- Keep existing migrations/schema and seed. No new auth flow or database changes.
- Add the three non-secret Sandbox keys to local .env; never print or modify
  existing certificate passwords, connection credentials or signing material.

## Plan and verification
1. Run failing Production-mode code/PKCE/UserInfo/isolation tests and HTTPS option
   regression tests. Retain disabled/default-off behavior on both environments.
2. Replace unconditional environment disable with explicit flag + HTTPS validation.
3. Run focused Sandbox and SSO tests, then concurrent Chrome E2E using a
   Production-mode host with a disposable local PostgreSQL database.
4. Update .env.example, deployment runbook, ADR and handoff with actual results.

## Structure and commands
Use existing SandboxTestOptions registration, SandboxTestIdentityTests and
SsoProtocolTestHost; follow existing typed options and xUnit styles.
From repo root: .codex-tmp/sdk8-check/sdk/dotnet.exe test
Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 -nr:false
-p:UseSharedCompilation=false --filter FullyQualifiedName~SandboxTestIdentityTests
Browser: build tools/AuthBridgeE2EHost/AuthBridgeE2EHost.csproj, then node
tools/AuthBridgeE2E/run-fixture.mjs tools/AuthBridgeE2EHost/bin/Debug/net8.0/AuthBridgeE2EHost.dll.
Tests never load application .env or DB. The retired SandboxIdentityE2E/SsoE2EHost
fixtures have been removed; historical handoffs retain their original paths.
