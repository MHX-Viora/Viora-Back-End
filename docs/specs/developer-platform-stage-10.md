# ANKT Developer Platform — Stage 10 specification

Status: **LOCAL EXECUTION AUTHORIZED**, 2026-10-23.
Authorization: user requested “kiểm tra và làm hoàn thiện giai đoạn 10”.
This covers the existing isolated audit/fix plan; production and major trust
changes remain excluded. Execution evidence is recorded separately from approval.
Source: user-supplied Stage 10 requirements I–XXXIV, received after Stage 9.
This document authorizes no implementation or production action by itself.
[Plan](../plans/developer-platform-stage-10.md) ·
[Initial evidence inventory](../testing/developer-platform-stage-10-audit.md).

## 1. Assumptions

- Work is limited to development and owned isolated fixtures. No production
  deployments, migrations, DNS changes, key rotation, service restarts or rollout.
- Preserve all pre-existing changes in the three repositories. No automatic
  commits, pushes or workflow dispatches; current push-to-main workflows deploy.
- Use the integrated Expo portal. Historical separate Next portal instructions
  in Stage 1/2 are superseded by the integrated-frontend handoff.
- Prior acceptance proves only its documented environment and checkpoint.
  Stage 8 native and Stage 9 sample/hosting gaps remain open until directly tested.
- Existing native gates stay closed. A new browser/native SSO trust contract,
  schema redesign or major architecture change requires a revised approved plan.
- Use actual local SMTP, PostgreSQL, issuer, HTTPS partner, account, BFF, review
  and runtime components for mandatory E2E. Test-owned identities only. Unit
  doubles are supplementary and cannot satisfy E2E acceptance.
- No new numeric SLO or deployment capacity is assumed. Collect measured local
  baselines first; propose thresholds only with known staging/VPS resources and
  human review. Local measurements do not certify production capacity.

## 2. Objective

Produce a reproducible, evidence-based assessment of the Stage 1–9 platform:
registration through domain proof, Sandbox, review, publication, discovery,
partner website use and supported Public/Confidential SSO. Reproduce and repair
confirmed defects with minimal changes and regression tests. Deliver an explicit
GO/NO-GO decision, release checklist and deployment/incident/rollback runbook.
An honest NO-GO with actionable blockers is valid audit output; it is not full
platform acceptance or permission to release.

## 3. Tech stack and project structure

| Location | Responsibility |
| --- | --- |
| `viora-BE/viora-BE` | ASP.NET Core .NET 8 API startup and endpoints |
| `viora-BE/Viora.Application`, `Viora.Domain`, `Viora.Infrastructure` | Application boundaries, entities, EF Core/Npgsql/OpenIddict persistence |
| `viora-BE/Viora.Application.Tests` | xUnit, actual HTTP/DB integration, disposable PostgreSQL helper |
| `viora-BE/tools` | Existing owned issuer/HTTPS partner/browser/executor fixtures |
| `viora/features/developer`, `viora/server/developer` | Expo 54 / React 19 Developer UI, static docs and Node BFF |
| `viora/features/mini-apps`, `viora/modules` | Production host and platform adapters; resolve exact module paths before edits |
| `viora/packages/miniapp-sdk` | Private 0.1.0 SDK; openid-client 6.8.8 |
| `viora-admin` | Existing React/Vite Admin application |
| `viora-BE/docs` | Canonical Stage 10 audit, spec, plan, release/runbook and handoff |

Keep existing explicit DTOs, dependency injection, transactional state transitions,
ownership predicates and error conventions. Example testing style:

```csharp
[Fact]
public async Task Foreign_owner_cannot_publish_approved_version()
{
    // Reuse actual owned fixture and persisted identities; assert denial and
    // unchanged publication. Implement against existing helpers after approval.
}
```

This is a style illustration, not an implemented test or evidence.

## 4. Architecture and security review

Trace all 18 components in requirement IV to actual routes, policies, services,
transactions, stores, clients and fixtures. Identify the registry credential
authority and OpenIddict projection synchronization, separate Developer/Admin/
Social sessions and Sandbox/Production authority, immutable approved snapshots,
launch revocation, consent/account-link subject provenance and SDK/Bridge trust.
Review for duplicate authorization/credential logic and browser secret exposure.

Threat-model all 11 surfaces in requirement V. Maintain findings with severity,
precondition, affected source, reproduction, impact, mitigation, regression and
residual risk. Cover every listed authentication, authorization, OAuth/OIDC,
SSRF/domain, WebView/Bridge and publishing case. Unsupported refresh grants and
token families receive an explained N/A, never fabricated test success.

Inspect configuration without printing values. Record file/key names and risk;
never include credentials, token/code/verifier/cookie contents, private keys or
raw potentially sensitive logs. Suspected exposed credentials require an incident
record and operator rotation action; do not rotate production credentials.
Dependency advisories require current reachability/exposure triage. Do not apply
blind force upgrades or call all advisory counts exploitable product findings.

## 5. Commands and execution policy

Existing command entry points below are proposed reproduction commands, **not
Stage 10 executed results**. Run from the indicated repository after approval,
capture sanitized summaries under `.codex-tmp/stage10-*`, and record revision,
dirty-state scope, tool versions, environment and exit codes.

```powershell
# viora-BE — installed isolated SDK, not the configured application database
$env:DOTNET_ROOT = (Resolve-Path .codex-tmp/sdk8-check/sdk).Path
$env:MSBUILDDISABLENODEREUSE = '1'
& "$env:DOTNET_ROOT/dotnet.exe" build viora-BE.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false
& "$env:DOTNET_ROOT/dotnet.exe" test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false
node tools/MiniAppProductionE2E/run-fixture.mjs --non-sso-partner
node tools/MiniAppProductionE2E/run-fixture.mjs --runtime-security
node tools/MiniAppProductionE2E/run-fixture.mjs --confidential-partner --runtime-security
node tools/MiniAppReviewE2E/run-fixture.mjs --all-flows
node tools/MiniAppReviewE2E/run-fixture.mjs --confidential-partner --all-flows

# viora
node node_modules/typescript/bin/tsc --noEmit
npm run test:developer
npm run test:docs
npm run docs:check
node --import tsx --test --experimental-test-isolation=none features/sso/bootstrap.test.ts features/mini-apps/tests/*.test.ts features/mini-apps/tests/*.test.mjs components/wallet/wallet-summary-card.test.mjs
npm --prefix packages/miniapp-sdk test
npm run test:docs:e2e

# viora-admin
npm run build
npm run lint
```

Before full backend execution, audit fixtures/configuration loading and external
side effects, and select safe isolated tests. Verify E2E host builds and fresh
Expo/Admin/docs artifacts before runners; existing stale artifacts are not a
release candidate. Export with explicit fixture-only API URL and disabled native
flags, following the Stage 9 operations guide. Never deploy fixture output.
If restore or a subprocess is blocked, change strategy and report the actual
limitation; a cached audit or no-restore build is not a vulnerability scan.
Additional migration/restore/load commands must be added after inspecting tool
contracts and safe boundaries. Never run `database update` against appsettings.

## 6. Testing and database strategy

- Build a Stage/feature/status/evidence matrix with implemented-and-verified,
  implemented-unverified, partial, missing, blocked and deprecated classifications.
  Keep historical evidence separate from fresh Stage 10 runs.
- Inspect all existing migrations and model mappings. Apply the complete chain
  to a disposable PostgreSQL cluster; verify constraints, indexes, concurrency,
  transactions and upgrade from synthetic representative old data. Inspect
  generated SQL. Never remove historical migrations or use SQLite as PG proof.
- Back up the disposable database and restore into a separate owned database;
  compare expected schema/data/integrity and measure local recovery duration.
  Test backup/key recovery procedures without accessing live private keys.
  Report local restore timing separately from unapproved production RPO/RTO.
- Backend integration covers all 25 requirement-X operations and relevant
  success/validation/auth/ownership/missing/conflict/expiry/revocation/race cases.
- Frontend covers the listed Developer/Admin/Utilities flows and Wallet/auth
  regressions. No redesign outside fixes. Browser/network checks must use real
  exported artifacts and actual account/issuer/session flows.
- E2E A–E have individual assertion maps. Extend existing real fixtures where
  gaps exist; do not infer coverage from runner exit alone. Add direct real-issuer
  runs of the Stage 9 standalone Public and .NET Confidential samples.
- Web, Android and iOS get independent evidence. Recheck device/tool availability;
  missing native SSO/device/TLS/permissions or Mac/Xcode is Blocked/Not Run.
  Do not weaken TLS/source/origin controls or open native gates to pass tests.
- New defects follow failing reproduction → minimal fix → targeted regression →
  affected broader suite. Avoid repeated unchanged test runs.

## 7. Performance and observability

Use bounded loopback/owned staging tests only. Record workload, ramp-up/steady/
ramp-down, sample counts, percentiles, errors, query time, CPU/memory, rate-limit
behavior, catalog/launch/authorize/token timings, browser load time and bundle
sizes. Bound duration/concurrency, redact secrets and provide stop controls.
Validate exact target ownership and safe resource ceilings before execution.
No production load; no invented numbers; no SLO pass/fail until reviewed.

Audit liveness versus DB-backed readiness, structured safe logs, correlation,
tracing and auth/OAuth/Sandbox/publishing metrics. Add small verified gaps after
approval. External metrics/alert delivery requires a connected system and an
executed delivery test; otherwise explicitly Not Run. Document retention/access.

## 8. Deployment, CI/CD and rollback

Read repository deployment configuration; do not assume it describes the actual
running VPS. Validate Nginx routing/BFF/static docs/host origins, forwarded headers,
HTTPS/CORS/CSP/cookies/HSTS/rates/limits, issuer/discovery/JWKS, keys, DB pooling
and permissions against available evidence. No SSH or authenticated production
access is assumed. Mark missing deployed evidence as Blocked/Not Run.

Propose and locally validate non-deploying verification pipelines/artifacts and
explicit production approval boundaries. Do not activate production workflows or
reconfigure the running VPS. Existing automatic deployment remains a release
risk until an enforceable gate is demonstrated; YAML alone cannot prove repository
environment approval settings. Obtain separate approval before changing release
trigger behavior or external repository settings.

Write commands adapted to confirmed paths/services in the deployment runbook;
unknown operational values are required operator inputs, not guessed commands.
Include previous artifacts, configuration/key backup, migration review, smoke
tests, incident response and rollback compatibility. Prefer application rollback
or forward-fix; migration Down is not presumed safe. Propose internal → developer
pilot → limited users → wider → GA with entry/stop/rollback criteria.

## 9. Boundaries

- Always: preserve pending work; fail closed; redact evidence; isolate resources;
  keep ownership and cleanup explicit; verify before reporting a result.
- Within approved implementation: tests/fixtures, confirmed small fixes,
  documentation, bounded local performance/restore drills and non-deploying
  checks, using existing architecture and dependencies where practical.
- Ask first: architectural/native SSO trust changes, destructive schema changes,
  major dependency migrations, production workflow behavior/settings changes,
  external accounts/resources or unknown-cost staging actions.
- Never: production mutations/load/deployment, delete user data, reset app DB,
  rotate live keys, change issuer/DNS, print secrets, dispatch deployment, publish
  SDK or enable global/native flags without separate authorization.

## 10. Success criteria and outputs

Deliver audit/threat model, migration and isolated restore results, A–E assertion
maps, backend/frontend/platform evidence, measured performance report, operational
and CI assessment, plus:

- `docs/releases/miniapp-platform-production-readiness.md`: all 16 groups with
  status, evidence, proposed owner, risk and required action.
- `docs/runbooks/miniapp-platform-deployment.md`: all 11 requested operational steps.
- `docs/handoffs/developer-platform-stage-10.md`: all 25 final topics, each Passed,
  Failed, Blocked or Not Run, commands, sanitized summaries and limitations.

Each applicable mandatory gate must have current environment-specific evidence.
Critical/High findings must be fixed or recorded as release blockers. GO requires
core E2E, PG migration, tested restore, rollback readiness, production configuration
evidence and explicit human approval. Until then use NO-GO; never equate completed
documentation or build success with production readiness.

## 11. Open questions and decision checkpoints

Initial planning requires no production access. Before the affected later tasks,
identify staging capacity/ownership, deploy artifacts/issuer origins, monitoring
delivery destination, Mac/Xcode/iOS device access, and operational owners. Audit
available sources first. Do independent local work while these remain unavailable.
Native SSO P0 design remains outside this approval until a concrete contract is
reviewed. Submit baseline-derived SLOs separately; no silent thresholds.
