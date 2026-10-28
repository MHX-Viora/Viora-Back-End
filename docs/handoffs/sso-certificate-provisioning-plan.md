# SSO certificate provisioning: specification and plan

## Objective and boundaries
Keep OpenIddict code flow, PKCE, token lifetimes, issuer, authentication schemes and previous-key rotation unchanged. Generate independent RSA-3072 signing/encryption certificates with protected PFX exports using .NET 8, without OpenSSL or external paid services.

Development may create a local owner-only certificate bundle when both explicit certificate paths are absent. Publish the complete pair by atomic directory rename on a local filesystem; concurrent creators reuse the winning pair. Invalid, incomplete or expired existing bundles must fail without regeneration. Never log key material, passwords or credential exceptions.

Production uses externally provisioned immutable files. No distributed secret store is configured in this repository; consequently no production startup generation is enabled. Render Secret Files are externally managed plaintext inputs, not an application-writable durable store. A standalone CLI mode provisions one reusable bundle before web/database startup. Base64 PFX files support Secret Files without runtime writes/decoding scripts. Existing binary PFX paths/passwords and previous-key configuration remain compatible.

## Structure and conventions
- `viora-BE/Sso`: provisioning service, standalone CLI, format support and existing options integration. Use explicit validation, scoped disposal and sanitized configuration errors.
- `.sso-certificates/`: ignored private owner-only output, excluded from Docker and publish output. Never commit its contents.
- `Viora.Application.Tests/SsoCertificateProvisioningTests.cs`: focused filesystem/host/protocol tests.
- `docs/operations/sso-certificates-render.md`: exact commands, deployment, recovery and rotation instructions.

## Ordered work and acceptance
1. Write failing provisioning tests for first run, concurrent creation, reload, production denial and immutable-file loading.
2. Implement atomic local provisioning and standalone `--provision-sso-certificates` mode; verify valid protected independent keys and no overwrite.
3. Integrate Development-only automatic provisioning and Base64 file loading; retain legacy paths and startup validation. Test wrong passwords, expiry, partial configuration and production restarts.
4. Exercise OpenIddict issuance/validation and Mini App SSO protocol regression tests, build backend, review changes and record actual limitations.

## Commands
`dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore -m:1 --disable-build-servers /p:UseSharedCompilation=false /p:BuildInParallel=false --filter FullyQualifiedName~SsoCertificateProvisioningTests`

`dotnet build viora-BE/viora-BE.csproj --no-restore -m:1 --disable-build-servers /p:UseSharedCompilation=false /p:BuildInParallel=false`

No database migration, new auth flow, production configuration mutation, certificate rotation, remote deployment or commit is authorized by this implementation task. A live Render redeploy/login must be verified by the operator after installing Secret Files.

## Completed implementation / handoff

- Added `SsoCertificateProvisioningService.cs`: owner-only local bundle, RSA-3072 separate purposes, password-protected exports, atomic directory publish, immutable reuse, complete-export validation and redacted errors.
- Added `SsoCertificateProvisioningCommand.cs`; `Program.cs` dispatches offline mode before `.env`/HTTP/database setup.
- Updated `SsoOptions.cs` / `SsoRegistration.cs`: Development-only startup generation, read-only conventional Render Secret Files in Production, Base64 in-memory import and existing binary/previous certificate compatibility.
- Updated `.env.example` with real non-secret issuer/origin and Render format; passwords intentionally blank, with directions to the private generated `render.env`.
- Updated `.gitignore`, `.dockerignore`, and project default item exclusions to protect output; no certificates/password files in publish output.
- Added focused provisioning tests and `docs/operations/sso-certificates-render.md` with deployment/recovery/rotation steps and primary references.
- Generated the real initial private bundle in `.sso-certificates/current`. It is ignored by Git and Docker. Verified the final published CLI reused both existing certificate files byte-for-byte; no secrets printed. No changes to the real backend `.env` during this feature task.

## Actual verification

- Initial red tests failed because provisioning types were absent. Windows sandbox denied restricted ACL creation; tests were then run with host filesystem permissions, without weakening ACL protection.
- All SSO-filtered regression tests: **175 passed**, including PostgreSQL and Mini App protocol flows.
- Final focused provisioning + registration run after complete-bundle validation: **35 passed**, including two CLI processes, protected independent keys, reload, production multi-instance/restart identity validation, missing/invalid/expired credentials, no repair/rotation of corrupt exports, and authorization-code/PKCE/ID-token validation using generated keys.
- Backend build: **0 warnings / 0 errors**. Final publish succeeded; output scan found no PFX/Base64/password/bundle artifacts. Git diff whitespace check passed.
- CLI output checked against synthetic private passwords/key exports; no credential values were emitted. Startup credential errors retain no inner exceptions. Newly added provisioning code has no logging of secrets.
- Review checked key independence, fail-closed behavior, disposal/zeroing, atomic publish, legacy path/rotation compatibility, secret exclusions and CLI-before-startup behavior.

## Remaining operator verification

No Render secret/configuration was changed, no live deploy was run and no real user was logged in. Verify public HTTPS proxy scheme handling, discovery/JWKS, unchanged key IDs across an actual Render deploy, and real partner callback login after installing the same Secret Files/passwords. Unix mode assertions are conditional; this run was Windows, not a Linux/Docker execution. Production startup generation against a distributed store is intentionally unavailable because no durable distributed secret provider is configured; the standalone provisioning + Render-managed secrets route is the supported production mechanism.
