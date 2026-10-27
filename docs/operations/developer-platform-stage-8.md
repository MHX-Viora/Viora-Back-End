# Stage 8 runtime operations — implementation checkpoint

Not release acceptance. Keep `MiniAppRuntime:Enabled=false` until its required
schema, matching API/host/app artifacts and platform/security checks pass.
Stage 7 review/publishing authority must stay enabled whenever runtime is enabled.
No application database migration or deployment has been performed by this work.

## Catalog reconciliation

`tools/MiniAppCatalogReconcile` processes one explicitly selected public AppId.
The default is dry-run. It never reads application configuration or creates an
approval/publication; the target must already be an actually approved, published,
currently safe immutable version. A missing projection stays hidden until verified
reconciliation or a later actual publish creates it. Review each application;
there is no blind full-registry write/backfill command.

Only after separate operator authorization, provide the reviewed connection via
`ANKT_CATALOG_RECONCILE_CONNECTION` using the existing secret mechanism, never a
command argument, committed file or log. Apply the separately reviewed additive
migration before reconciliation; the tool never migrates a database.

```powershell
dotnet run --project tools/MiniAppCatalogReconcile -- --app-id=<actual-public-app-id>
dotnet run --project tools/MiniAppCatalogReconcile -- --app-id=<actual-public-app-id> --apply
```

Output is only AppId/status/written. Exit 0 means ready/already-present/applied;
1 means unavailable/retry-required/failure; 2 means invalid explicit configuration.
`--apply` uses a serializable transaction, exact target validation and immutable
projection guard. A conflict needs fresh dry-run/review, not automatic blind retry.
No source connection, token, code, secret, private Developer identity or error
stack is printed. CLI has no default database, force approval or publish switch.

## Rollback boundary

Runtime cleanup is opt-in through `MiniAppRuntime:Enabled=true` or independently
`MiniAppRuntime:CleanupEnabled=true`. Both default false. The application only
registers the worker when either is configured; no migration or database selection
is performed by the worker. Keep CleanupEnabled true after disabling endpoint/UI
access on an already migrated deployment so retention continues during rollback.
Retention remains bounded to 1–30 days (default 7), including cleanup-only mode.
Each one-minute tick processes at most 100 rows per cleanup category; expired
unredeemed grant receipts are erased and their waiting sessions become terminal.
Historical session/grant rows are removed after expiry plus retention when no
retained runtime event still references the session. Stage 7 and Social records
are preserved. Cleanup logs contain aggregate counts, not identities or payloads.

Module-specific per-minute HTTP limits are configured under `MiniAppRuntime`:
`ReadLimitPerMinute` (default 120, range 1–1000), `PrepareLimitPerMinute` (30,
1–30), `LaunchLimitPerMinute` (20, 1–20), `RedeemLimitPerMinute` (30, 1–30), and
`RuntimeLimitPerMinute` (120, 1–120). These policies are independent and leave
other endpoint/auth policies unchanged. `LaunchLimitPerMinute` also bounds new
sessions per binding and authenticated account after prior sessions have closed.
`EventsPerSessionPerMinute` defaults to ten and accepts 1–10. Enabled runtime
startup rejects zero, negative or over-ceiling values; quotas cannot be disabled.

Per-IP launch throttling is combined with that binding/account creation limit
and at most three active sessions per binding. Unredeemed sessions stop
occupying active slots when their 30-second grant expires. Client event ingress
accepts only BRIDGE_READY, BRIDGE_UNAVAILABLE and PERMISSION_DENIED, at most ten
events/minute/session. These events do not approve, publish, authenticate or grant
native permissions. No arbitrary telemetry text or URLs are stored.

Disable Stage 8 and dispose/revoke host sessions/grants before separately approved
schema downgrade. Catalog downgrade loses its derived projection rows, not the
Stage 7 review/publication or social identity state. Keep the Stage 7 runtime
publication gate; turning Stage 7 off is not a safe runtime rollback.
Native P0/device verification remains pending. Actual Web host business evidence
and scope limits are recorded in the final Web checkpoint below.

## Web artifact preparation

Frontend runtime defaults off. Set `EXPO_PUBLIC_MINI_APP_RUNTIME_ENABLED=true`
only for an explicitly configured environment; native remains unavailable.
`EXPO_PUBLIC_MINI_APP_HOST_ORIGIN` must be an exact dedicated HTTPS origin,
separate by hostname from app/API/issuer/Developer/Sandbox. Configure the same
origins on the backend runtime options. `EXPO_PUBLIC_API_URL` is the API origin;
`EXPO_PUBLIC_APP_ORIGIN` is the app origin (build policy can fall back to existing
DEVELOPER_SITE_ORIGIN); EXPO_PUBLIC_OPEN_API_ISSUER is mandatory for enabled builds.
No value is inferred from route params or partner messages.

From frontend root, `npm run build:web:mini-apps` prepares the Expo Web artifact
and composed vercel.json in `.codex-tmp/stage8-web-export`. `-- --config-only`
validates/generates headers without export. It never deploys, migrates or invokes
Developer BFF for public catalog/launch/host APIs. Preserve generated exact-route
CSP when packaging; ordinary build:web does not generate that composed policy.
For isolated verification use EXPO_NO_DOTENV=1 and explicit fixture origins.
The deployment builder clears Metro's cache before export: cached inlined
`EXPO_PUBLIC_*` values must not retain a prior API/host origin while generated
CSP changes to the new origin.

Review shared persistent Data Protection key storage before any approved rollout;
ephemeral or per-instance key rings cannot reliably share host bindings/receipts.
Browser third-party cookie restrictions and partner frame-ancestors must be tested
with every required ancestor; UI loading alone proves neither bridge nor SSO.

## Isolated Web host checkpoint

Build the actual frontend export from `../viora` using only fixture origins:

```powershell
$env:EXPO_NO_DOTENV='1'
$env:EXPO_PUBLIC_MINI_APP_RUNTIME_ENABLED='true'
$env:EXPO_PUBLIC_MINI_APP_HOST_ORIGIN='https://production.ankt.localhost:5593'
$env:EXPO_PUBLIC_API_URL='https://identity.ankt.example.com'
$env:EXPO_PUBLIC_OPEN_API_ISSUER='https://identity.ankt.example.com'
$env:EXPO_PUBLIC_APP_ORIGIN='https://portal.ankt.localhost:5594'
$env:DEVELOPER_SITE_ORIGIN='https://portal.ankt.localhost:5594'
$env:ANKT_SANDBOX_HOST_ORIGIN='https://sandbox.ankt.localhost:5493'
npm run build:web:mini-apps
```

Build `tools/SsoE2EHost/SsoE2EHost.csproj` in Release using the local .NET 8 SDK,
then from backend root run `node tools/MiniAppProductionE2E/run-fixture.mjs`,
and separately add `--confidential-partner` for that publication/client variant.
The runner reuses actual Developer/Admin login, Stage 6 executor and review/
publication UI before testing the exported Web host and SDK v1 context/close.
These commands use disposable PostgreSQL, generated TLS/keys, private loopback
services and temporary Chrome profiles. They do not read application settings,
migrate a live database, send email or deploy. Keep their fixed fixture ports free.

## Completed isolated Web verification

After the export and Release fixture build above, run sequentially from backend:

```powershell
node tools/MiniAppProductionE2E/run-fixture.mjs --runtime-security
node tools/MiniAppProductionE2E/run-fixture.mjs --confidential-partner --runtime-security
node tools/MiniAppProductionE2E/run-fixture.mjs --non-sso-partner
node --test tools/MiniAppProductionE2E/production-partner.test.mjs tools/MiniAppReviewE2E/owned-browser-cleanup.test.mjs tools/ProductionHostTests/*.test.mjs
```

First two commands include actual embedded Production SSO, consent variants,
reload/reopen, hostile Web messages/media policy, observed performance and live
Admin suspension/recovery. Third separately publishes a non-SSO app without a
Production OAuth client, uses actual Social password login and WalletService,
and verifies Utilities return/focus refresh. Unrelated authenticated background
services are outside that isolated fixture and can log missing DI dependencies;
do not infer whole-application acceptance or silence those errors as success.
All three final runners exit 0 and clean up their owned loopback processes/profiles.

Public partner credentials stay in SDK memory; Confidential tokens stay in the
partner backend behind an opaque HttpOnly cookie. Its popup completion is only a
receipt: iframe must fetch fresh UserInfo-backed session status. Partner fixture
transaction/session stores are bounded and short-lived, not a production session
store recommendation. Never copy fixture accounts/keys/TLS settings into rollout.

User chose Web first on 2026-10-23; Android/iOS **NOT VERIFIED**, runtime default
off. No native permission enablement or native SSO broker is authorized by that
choice. Review exact deployed origins/CSP/frame ancestors, shared Data Protection
keys, browser third-party cookies and operational database/migration plan before
separately approved rollout. Never use test fixture certificate trust as a TLS bypass.

For model-only verification set `VIORA_EF_DESIGN_CONNECTION` to a synthetic
loopback port-1 fixture connection before `dotnet ef migrations has-pending-model-changes`;
the existing design-time factory then does not load application credentials.
This check does not migrate/connect to a live database. Existing ReportReason
sentinel warning is unrelated to Stage 8 and remains recorded in the handoff.

See [Web acceptance and limits](../handoffs/developer-platform-stage-8-acceptance.md)
and [performance observations](../testing/developer-platform-stage-8-performance.md).
