# Stage 10 — audit and evidence inventory

2026-10-23. Local execution authorized by “kiểm tra và làm hoàn thiện giai đoạn 10”.
**NO-GO for production.** The user confirmed there is no separate staging,
Mac/iOS environment or external monitoring available. This does not block local
verification. No production access, deployment, migrations, key rotation or push.

Current evidence and open gates:

- [Final handoff and 25-topic assessment](../handoffs/developer-platform-stage-10.md).
- [Architecture/control map](developer-platform-stage-10-architecture.md).
- [Threat model, findings and dependency triage](developer-platform-stage-10-security.md).
- [Database/upgrade/restore](developer-platform-stage-10-database.md).
- [Measured local baseline](developer-platform-stage-10-performance.md).
- [Release checklist](../releases/miniapp-platform-production-readiness.md) and
  [deployment/incident/rollback runbook](../runbooks/miniapp-platform-deployment.md).

Builds/tests below the historical boundary are not reused as fresh passes.
Fresh commands/logs are indexed in the handoff. Fixture artifacts remain local;
legacy `stage7/8/9` output directory names were rebuilt during Stage 10.

## Historical planning inventory (retained for provenance)

2026-10-23. **Planning-only source review; not the completed Stage 10 audit.**
No Stage 10 build, E2E, load, migration, restore or production verification has
been executed. Prior results below are historical checkpoint evidence, not fresh
Stage 10 passes. Detailed source/control/assertion review is planned in Tasks 01–05.

## Stage matrix (initial classification)

| Stage | Feature | Implemented | Verified | Blocked / remaining | Evidence |
| --- | --- | --- | --- | --- | --- |
| 1 | Public Developer foundation | Yes; original separate portal architecture deprecated | Historical HTTP/UI checks | Actual deployed route not certified | [Stage 1](../handoffs/developer-platform-stage-1.md); [current integrated architecture](../../../viora/docs/handoffs/developer-integrated-fe.md) |
| 2 | Independent Developer accounts, BFF sessions | Yes; historical Next references superseded | Historical real account/SMTP/BFF checks; Stage 9 browser account regression | Production mail/cookies/session configuration not certified here | [Stage 2](../handoffs/developer-platform-stage-2.md), [Stage 9](../handoffs/developer-platform-stage-9.md) |
| 3 | Registry, credentials, logo | Yes | Historical real backend/portal/storage fixture checks | Fresh cross-owner/rotation/secret exposure assessment pending | [Stage 3](../handoffs/developer-platform-stage-3.md), [plan](../plans/developer-platform-stage-3.md) |
| 4 | Integration and domain proof | Yes | Historical controlled HTTPS proof; initial public-domain/PG gaps need reconciliation against later evidence | Public Internet domain deployment not certified | [Stage 4](../handoffs/developer-platform-stage-4.md), [plan](../plans/developer-platform-stage-4.md) |
| 5 | OAuth/OIDC, consent, SDK | Yes, local scope; SDK remains private | Historical real Public/Confidential protocol/browser + PG evidence | Native SSO and actual production issuer/key configuration not certified | [Stage 5](../handoffs/developer-platform-stage-5.md), [spec](../specs/developer-platform-stage-5.md) |
| 6 | Sandbox and trusted executor | Yes, controlled Web scope | Historical actual iframe/issuer/partner/executor evidence | Deployed origins/TLS/headers and native not certified | [Stage 6 acceptance](../handoffs/developer-platform-stage-6-acceptance.md) |
| 7 | Version/review/moderation/publishing | Yes, local acceptance | Historical real Public/Confidential A–D; disposable PG migration/races | Fresh full-chain/old-data/restore validation and deployed approval gates pending | [Stage 7 acceptance](../handoffs/developer-platform-stage-7-acceptance.md) |
| 8 | Catalog, Production Web host | Yes, local Web acceptance | Historical actual Utilities/Wallet/host/SSO/moderation/security | Production/cross-browser certification pending | [Web acceptance](../handoffs/developer-platform-stage-8-acceptance.md) |
| 8 | Android/iOS native host | Partial; experimental and gated | Android debug build and Node/TS tests only | Device permission/lifecycle/SSO proof; native trust P0; actual iOS build/runtime | [Native handoff](../handoffs/developer-platform-stage-8-native.md), [SSO decision](../decisions/stage-8-native-sso.md) |
| 9 | Public docs and reference | Yes; local verification | Historical 20-page static/browser/account E2E; 14 docs checks, selected backend checks | Direct new .NET/standalone Public sample issuer E2E, actual hosting, full browser matrix | [Stage 9 handoff](../handoffs/developer-platform-stage-9.md) |

## Source observations that shape the plan

- Canonical later-stage specs/plans/handoffs are in `viora-BE/docs`; Stage 1/2
  plans are in `viora/docs/plans`. Missing earlier spec files must be recorded,
  not reconstructed as historical approvals. Current portal integration supersedes
  historical independent Next architecture.
- `Viora.Application.Tests/IsolatedPostgres.cs` starts its own GUID-named cluster
  using PostgreSQL 18 binaries by default, loopback port and generated connection
  string. It does not accept the application's connection string. Full-chain,
  old-data and backup/restore acceptance still require direct new evidence.
- `tools/MiniAppProductionE2E/run-fixture.mjs` delegates to the existing review
  fixture in Production mode. The review runner recognizes Public/default,
  `--confidential-partner`, `--non-sso-partner`, `--runtime-security` and
  `--all-flows` options. Map individual assertions before claiming A–E coverage.
- The Stage 9 .NET sample builds, but prior Confidential E2E used a different
  partner implementation. Those results cannot certify the new middleware sample.
- FE and BE `.github/workflows/deploy.yml` trigger on main pushes and dispatch.
  Inspected deployment jobs have no YAML `environment` approval boundary.
  External GitHub protection settings were not inspected; do not claim they are
  absent. No workflows have been changed or executed in this planning pass.
- Deployment files reference `/var/www/viora/frontend`, `/var/www/viora/backend`,
  compose root `/var/www/viora`, service `viora-api` and public frontend
  `https://mxh.ankt.vn`. These are repository observations, not verified current
  VPS paths/service state, issuer origin or approval to access production.
- Existing Stage 9 frontend audit reports 39 advisories, including 33 high.
  Fresh advisory/exposure triage is required; this count alone does not establish
  33 exploitable platform defects or their remediation status today.
- Planning inventory found local `adb`, PostgreSQL `pg_dump.exe` and the local
  .NET8 SDK path. Device readiness, simulator/TLS trust, monitoring access and
  actual staging capacity were not tested. Docker/k6 were not found on PATH in
  this inventory; existing Node/.NET tooling can be assessed before adding tools.
- Working-tree inventory: 303 frontend, 286 backend and 13 Admin status entries.
  Counts include pre-existing work and are not Stage 10 changes. Preserve them.

## Initial release assessment

**NO-GO / evidence incomplete**, not a completed security verdict. Native P0,
fresh mandatory A–E verification, complete PG upgrade/restore evidence, operational
configuration/monitoring and release approval remain to be assessed. Findings
are not automatically resolved by beginning a new stage.

The approved implementation must deliver the detailed matrix, threat model,
evidence index, 16-group release checklist, deployment runbook and 25-topic final
report. See the [authorized plan](../plans/developer-platform-stage-10.md).
