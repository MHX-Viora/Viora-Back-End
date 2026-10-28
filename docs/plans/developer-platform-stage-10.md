# ANKT Developer Platform — Stage 10 implementation plan

Status: **LOCAL AUDIT DELIVERED — NO-GO; ACCEPTANCE GAPS OPEN**, 2026-10-23.
[Specification](../specs/developer-platform-stage-10.md) ·
[Initial audit](../testing/developer-platform-stage-10-audit.md).
User authorization: “kiểm tra và làm hoàn thiện giai đoạn 10”. The existing
development/isolated-test plan is being executed; this is not release approval.

## Execution contract

Execute sequentially. Each authored change targets at most approximately five
files; split any larger task before implementation. Tables list likely file
groups, not permission for unrelated edits. Use existing architecture and owned
fixtures. Each checkpoint updates evidence and reviews diffs; request another
decision only for stated scope/architecture/release boundaries. A blocked native
or external task must not prevent independent local audit work.

Shared verification commands are in the spec. New tool entry points get exact
commands recorded when authored; their names/results are not invented here.
Every test records environment, command, exit status, assertion coverage, summary
and sanitized log location. No Passed from source presence or historical counts.

## Ordered tasks

| ID | Task / dependencies | Acceptance | Verification | Likely authored files (maximum ~5 per slice) |
| --- | --- | --- | --- | --- |
| 01 | Evidence inventory / approval | Read Stage 1–9 spec/plan/handoffs where present; capture current revisions/dirty scope, tools and evidence freshness; identify historical/deprecated sources | Read-only inventory; verify every cited path | Audit, evidence index |
| 02 | Architecture review / 01 | Map all 18 components, policies, transactions and credential authority; list concrete gaps | Trace routes to services/stores and current fixtures | Audit, architecture report |
| 03 | Threat model / 02 | All 11 surfaces and requirement-V cases mapped; severity and reproducible evidence rules | Review entry points, boundaries, attack cases and N/A rationale | Security report, test map |
| 04 | Secrets and configuration audit / 02 | Scan tracked/config/build inputs without exposing values; assess key separation, rotation/recovery and session secrets | Redacted metadata-only scan; inspect current key configuration and log paths | Security report, incident/rotation instructions if needed |
| 05 | Dependency exposure triage / 03 | Current FE/Admin/SDK/NuGet advisory results or explicit blocked metadata; runtime/build exposure classified | Official advisory metadata and manifests; no cached-zero claim | Dependency report, evidence index |

Checkpoint A: review architecture/security findings and known P0/native blockers.
Do not change a major trust boundary or force dependency upgrades.

| ID | Task / dependencies | Acceptance | Verification | Likely authored files (maximum ~5 per slice) |
| --- | --- | --- | --- | --- |
| 06 | Safe test environment / 01,04 | Fixtures ignore application secrets/DB config; owned loopback processes, HTTPS and bounded cleanup; build current hosts | Review startup/side effects; build test hosts with isolated SDK | Existing fixture/startup helpers and their tests if fixes needed |
| 07 | Database model audit / 02 | Inventory migrations/tables/FKs/indexes/cascade/concurrency/retention and transaction boundaries | Model/source and SQL inspection; schema drift checks | Database report, focused tests |
| 08 | Complete migration chain / 06,07 | All migrations apply to empty disposable PG; constraints/indexes match expected schema | Actual PostgreSQL full-chain integration test, isolated cluster only | PG helper/test, migration report |
| 09 | Old-data upgrade and races / 08 | Synthetic old checkpoint upgrades without loss; transaction and concurrent publishing/ownership invariants hold | Existing PG race tests plus focused missing cases; SQL review | Upgrade/race tests, database report |
| 10 | Backup/restore drill / 08 | Owned pg_dump restored into separate owned target; data/schema/integrity compared; failure cleanup bounded | Execute real local dump/restore and validation; record measured duration | Restore fixture/test, recovery report |

Checkpoint B: database and recovery evidence. Never run production migrations or
assume destructive Down rollback is safe. Schema redesign requires a revised plan.

| ID | Task / dependencies | Acceptance | Verification | Likely authored files (maximum ~5 per slice) |
| --- | --- | --- | --- | --- |
| 11 | Backend identity/registry/domain / 03,06,09 | Requirement-X account, session, app/logo/client/rotation/revocation/callback/domain cases mapped and executed | Focused actual HTTP/DB tests; new defects RED/GREEN | Relevant backend tests, minimal corresponding fixes |
| 12 | Backend protocol/authorization / 03,06,09 | PKCE/state/nonce/issuer/audience/signature/replay/expiry/scope/consent/revocation/linking/env cases covered | Actual issuer integration and security suites; unsupported grants explained | Relevant SSO tests, minimal fixes |
| 13 | Backend lifecycle/runtime / 03,09 | Sandbox/review/decisions/publish/suspend/restore/catalog/launch and races deny bypass | PG + HTTP suites, including unchanged state after denial | Relevant lifecycle tests, minimal fixes |
| 14 | Full backend regression / 11–13 | Current safe complete suite builds/runs; every skip/failure has explicit disposition | Spec backend build/test commands after fixture side-effect audit | Evidence/report; no unnecessary test rewrites |
| 15 | Frontend integration regression / 06 | Developer/BFF/SDK/Admin/Utilities/Wallet current builds and suites checked; required UI gaps listed | Spec FE/Admin commands; browser checks for paths without automated coverage | Existing frontend tests, minimal UI fixes |
| 16 | E2E A, no SSO / 13,15 | All 15 steps use real account/HTTPS site/domain/Sandbox/review/publish/Utilities/host/close | Non-SSO production runner with step-level map and browser/network evidence | Existing non-SSO runner/assertions, evidence map |
| 17 | E2E B, Public / 12,13,15 | All 17 steps, consent revoke/re-login and reopen checked with real issuer/browser; session behavior documented | Public production runner plus any uncovered assertions | Public E2E modules, evidence map |
| 18 | E2E C, Confidential / 12,13,15 | All 12 steps; backend exchange and partner cookie; no secret in browser responses/bundle | Confidential production runner, real browser/network inspection | Confidential E2E modules, evidence map |
| 19 | Stage 9 direct sample E2E / 17,18 | Standalone Public and actual .NET middleware samples run through issuer, ID token/UserInfo/session/logout; source matches docs | Real HTTPS trusted fixture, build sample and browser protocol assertions; no TLS bypass | Sample fixture adapter, sample-specific tests, docs if corrected |
| 20 | E2E D, review/publishing / 13,15 | All 15 steps including request-changes/new version, immutable live draft separation and restore-unpublished | Both real review runners and exact version assertions | Review E2E assertions, evidence map |
| 21 | E2E E, hostile cases / 11–18 | All 17 listed cases mapped to actual boundary tests; browser-specific cases use browser evidence | Runtime-security runner + PG/HTTP negative cases; redacted leak scan | Security E2E/assertions, report |

Checkpoint C: A–E coverage is assessed from individual assertions. Existing
runner reuse does not automatically prove every new requested step. Fix confirmed
defects incrementally; run affected regressions before continuing.

| ID | Task / dependencies | Acceptance | Verification | Likely authored files (maximum ~5 per slice) |
| --- | --- | --- | --- | --- |
| 22 | Web/docs hosting verification / 15,19 | Direct routes/refresh/auth/CSP/iframe/callback/responsive/error checks on fresh artifacts; distinguish local routing from deployed proof | Actual browser + raw HTTP/docs E2E; inspect production hosting config read-only if available | Browser checks, hosting report, minimal routing fixes |
| 23 | Android verification / 03,06,17,18 | Recheck emulator/device, trusted TLS and OS permissions; run supported lifecycle/navigation/security/SSO; record unresolved trust P0 | Actual Android build/device assertions; otherwise Blocked/Not Run; no gate bypass | Native test evidence and small proven fixes only |
| 24 | iOS verification / 03,06,17,18 | Record actual Mac/Xcode/device availability; build and WKWebView/deep-link/session/permission flows only if available | Real Xcode/device results; Windows typechecks are not iOS proof | iOS evidence, small proven fixes if executable |
| 25 | Baseline and bounded load tooling / 06,16–18 | Collect isolated API/DB/browser/bundle baseline; six workload shapes, resource monitoring, ramp phases and hard stop controls | Verify owned exact target, duration/concurrency bounds and redaction; run low bounded load; record percentiles/errors/CPU/RAM | Load fixture, safety tests, performance report |
| 26 | SLO proposal and performance fixes / 25 | Baseline/resources stated; human-review SLO proposal; optimize only measured bottlenecks | Repeat affected workload only after a change; no numeric SLO acceptance before review | SLO report, proven localized fix/tests if needed |
| 27 | Observability readiness / 03,06,14 | Health vs readiness, safe logs/correlation/tracing/metrics/retention mapped; missing checks get bounded implementation | Actual health/DB-failure and no-secret-log tests; external alert delivery only if connected | Startup/health/metrics slice + tests, operations report |
| 28 | Production configuration and CI audit / 04,05,22,27 | Actual vs repository-only TLS/DNS/Nginx/issuer/JWKS/headers/pooling/key evidence separated; CI approval/artifact gaps identified | Read-only config review and locally validated non-deploying checks; no deploy dispatch | Configuration report, proposed CI checks; existing deployment behavior changes require separate approval |
| 29 | Release and deployment runbook / 10,14,16–28 | 16 release groups with status/evidence/owner/risk/action; 11 runbook steps; artifacts/schema compatibility/recovery/staged rollout explained | Cross-check real paths/services and commands; unknown operational inputs explicit; run local smoke/rollback compatibility where possible | Required release checklist, deployment runbook, recovery/staged rollout notes |
| 30 | Final review and handoff / 01–29 | 25 requested topics each Passed/Failed/Blocked/Not Run; open findings and evidence freshness recorded; explicit GO/NO-GO | Review scoped diffs, current evidence map, Markdown links/redaction; no new unchanged test reruns | Stage 10 handoff, audit/security/release updates |

Checkpoint D: local audit completion and release eligibility are separate. Keep
NO-GO for applicable mandatory blockers, failed/unrun core E2E, migration/restore
or missing production configuration approval. No deployment follows automatically.

## Rollback considerations

Keep each fix reviewable and reversible without discarding unrelated changes.
Do not run git reset/clean against dirty repositories. Test-owned resources use
verified GUID roots, explicit process ownership and bounded lifetimes. Never stop
unrelated browsers, services or databases. Retain failed-run sanitized evidence.
No application migration is expected merely to audit existing stages; justify
and review any schema change. Prefer compatible application rollback/forward-fix.

## Initial risks and decisions

| Risk | Impact | Response |
| --- | --- | --- |
| Native SSO trust P0 and unaccepted native security | Cross-platform release blocked | Preserve native gates; concrete contract review before architectural changes |
| No proven Mac/Xcode/iOS access | iOS acceptance may remain Blocked | Report platform evidence separately; continue independent Web/DB audit |
| Existing dependency advisories | Potential High release blockers | Fresh advisory/exposure triage; targeted compatible fixes, no blind upgrade |
| Production deployment workflows on main | Unintended release if pushed | No push/dispatch; propose enforceable approval changes for separate review |
| Current hosting/monitoring/backups not connected | Operational gates unverified | Distinguish repository plans/local drills from deployed proof |
| Many pre-existing dirty files | Accidental overwrite or ambiguous artifact provenance | Preserve work, record baseline and exact scoped diffs/artifact inputs |

The user authorized this spec and development/isolated-test plan. Production
actions and major trust/architecture changes remain excluded.

## Execution assessment — 2026-10-23

Evidence and exact commands: [handoff](../handoffs/developer-platform-stage-10.md).
Each planned task has been assessed; this table does not mark missing mandatory
acceptance as complete. No staging/Mac/iOS/external monitoring is available,
confirmed by the user. Verdict: NO-GO.

| Tasks | Disposition / outstanding acceptance |
| --- | --- |
| 01–04 | Local source/provenance/architecture/threat/config inventory delivered; history/live-secret/production-key audit remains outside available evidence |
| 05 | Fresh FE/Admin/SDK/NuGet scans and compatible patches; FE 38 advisories and test-native-SQLite advisory still need closure/disposition |
| 06 | Owned Release hosts and fresh local browser artifacts built; cached Debug attempt excluded |
| 07–10 | Actual PG chain, model drift, synthetic old-data upgrade, races and separate DB dump/restore pass; production-volume/role/key recovery not certified |
| 11–14 | Backend full regression 845 passed/one explicitly isolated MoneyAudit skip; additional offline snapshot test passes |
| 15 | Frontend/Admin builds, types, lint and focused regressions pass; no deployed or full-Social-app claim |
| 16–18 | Non-SSO/Public/Confidential business/runtime-security runners exit 0; exact complete registration/public-hosting journey per A–E still needs environment-specific proof |
| 19 | Actual standalone Public/.NET middleware issuer/browser E2E Not Run; .NET sample build passes; dedicated trusted fixture adapters remain work |
| 20 | Public and Confidential all-flow review/publishing Release runners exit 0 |
| 21–22 | Web hostile-case/Chrome/docs/account evidence delivered; device/browser matrix and real deployed hosting remain open |
| 23–24 | Android emulator present; native trust/accepted dev build/TLS/permission acceptance blocked. iOS blocked without Mac/Xcode |
| 25–26 | Six bounded shapes/72 requests plus launch/resources measured; sustained successful OAuth load and reviewed production SLOs Not Run |
| 27 | DB-backed readiness implemented/tested; deployed logs/metrics/retention/alert delivery blocked |
| 28 | CI/config audit and proposed non-deploying check list delivered; actual external approval/proxy/hosting proof unavailable; workflows unchanged |
| 29–30 | 16-group release checklist, 11-step runbook, 25-topic handoff and local link checks delivered; operational rollback/restore and GO remain blocked |
