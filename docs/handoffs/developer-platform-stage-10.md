# Stage 10 — local audit, fixes and release assessment

2026-10-23. **NO-GO for production; full platform acceptance remains incomplete.**
User authorized “kiểm tra và làm hoàn thiện giai đoạn 10” and confirmed there is
no staging, Mac/iOS environment or external monitoring available. Local audit,
bounded fixtures, fixes and release documentation are delivered. Unexecuted
mandatory checks are explicitly retained below; they are not waived by this report.
No production access/load/deployment/migration, key rotation, SDK publication,
push, workflow dispatch or automatic commit was performed.

## Changes and verified behavior

- Added additive `GET /health/ready`: DB connectivity probe, 3-second cancellation,
  no-store and minimal 200/503 status body. `/health` remains liveness.
- Limited xUnit class parallelism to two in `TestParallelism.cs`. Three real
  `initdb` timeouts were reproduced before the change; full regression passes
  afterward. Within-test concurrency/race assertions remain enabled.
- Added actual PostgreSQL full-chain/old-data/backup/restore/schema/integrity
  verification and an offline current-model/migration-snapshot check.
- Added opt-in fixed-target, bounded six-shape load baseline plus a safety test,
  protected fixture-only CPU/RAM samples and metadata-only source/config inventory.
- Excluded local credential config, service-account JSON and private certificate/
  key files from Docker context. Source review only; Docker is unavailable here.
- Applied compatible lockfile updates: FE `follow-redirects 1.16.1`; Admin Vite
  `8.3.5`, `follow-redirects 1.16.1` and compatible Vite transitive updates. No
  major migration/force upgrade. Admin audit now zero; FE remains 38 advisories
  (32 High/6 Moderate), SDK zero. NuGet has a test-native-SQLite High advisory.
- Added architecture, threat model, E2E assertions, DB/recovery/performance reports,
  16-group release checklist and 11-step deployment/incident/rollback runbook.

## 25-topic assessment

“Passed” means the stated local scope only, never deployed/cross-platform acceptance.

| # | Topic | Status | Evidence / practical limit |
| --- | --- | --- | --- |
| 1 | Scope/architecture | Passed | Stage 1–9 handoffs reconciled with integrated portal; 18-component authority map |
| 2 | Developer identity/BFF | Passed | 63 tests; actual registration/local SMTP verification/login/Console browser |
| 3 | Registry/ownership/credentials | Passed | full API/service/rotation/revocation suites; live key custody remains unverified |
| 4 | Domain/SSRF/network | Passed | address/fetch/domain/Sandbox network suites and owned HTTPS partner proof |
| 5 | Public OAuth/OIDC | Passed | fresh Release real issuer/partner/production host/runtime-security E2E |
| 6 | Confidential OAuth/OIDC | Passed | fresh Release server exchange/partner session/production host/runtime-security E2E |
| 7 | Consent/linking | Passed | HTTP suites, real deny/retry/new-scope/reopen and source/session provenance |
| 8 | Sandbox/executor/evidence | Passed | actual 18 checks; isolated executor/browser/PG evidence suites |
| 9 | Version/review decisions | Passed | Fresh Release Public/Confidential changes/new draft/resubmit/rejection/approval, immutable live target and actual Admin interactions |
| 10 | Publishing/moderation/catalog | Passed | approved exact version, suspension revocation, restore-unpublished/republish; full PG races |
| 11 | Web host/Bridge | Passed | dedicated host/source/grant/context/media/navigation denial; Chrome close/reopen |
| 12 | SDK | Passed | 37 tests; current separate dependency audit zero; package remains private |
| 13 | Public docs/hosting locally | Passed | 20-page actual artifact/browser/SEO/404/search/account checks; hosting deployment unverified |
| 14 | Actual standalone reference-sample issuer E2E | Not Run | Public docs client and actual .NET middleware need dedicated trusted HTTPS browser fixture adapters. .NET sample fresh build passes; other partner fixtures do not certify it |
| 15 | Model/full migration chain | Passed | offline snapshot check; 71 migrations through AddMiniAppProductionAudit |
| 16 | Old-data upgrade | Passed | synthetic Developer identity/email/security revision survives upgrade; broader production dataset unverified |
| 17 | Concurrency/transaction/integrity | Passed | current full backend suite includes actual PG one-winner/lock/rollback/immutability cases |
| 18 | Backup/restore | Passed | real separate owned DB restore, 2,561 schema entries/history/data and restored FK denial; no production-volume/key/offsite drill |
| 19 | Frontend/Admin regression | Passed | types/lint/build; 50 Mini App/Wallet/SSO, 63 BFF, 14 docs, 10 Admin service and 40 host/partner tests |
| 20 | Security/dependency closure | Failed | compatible fixes applied, but remaining advisory reachability/dispositions and built-image secret inspection are open |
| 21 | Android acceptance | Blocked | connected emulator observed; native SSO P0, accepted dev build/TLS/permissions/security run unavailable; gate stays closed |
| 22 | iOS acceptance | Blocked | Windows; no Mac/Xcode/device access, confirmed by user |
| 23 | Performance baseline | Passed | 72 owned requests, six bounded shapes, CPU/RAM and browser launch samples; success-path sustained load/SLOs unaccepted |
| 24 | Observability/production configuration | Blocked | local readiness tests pass; actual proxy/issuer/keys/SMTP/metrics/retention/alert delivery unverified; no staging/monitoring |
| 25 | CI/release/rollback/rollout | Blocked | release checklist/runbook delivered; actual enforced approvals, composed deployed docs, compatible prior artifacts and operational recovery unproved |

## Fresh evidence index

All logs below are local `.codex-tmp/stage10/` under the named repo, excluded from
release artifacts. The three working trees contain extensive pre-existing work.
Revisions: BE `663e14a3`, FE `4c8a62f`, Admin `9e1e6d4`; a commit alone does not
identify these dirty artifacts. `inventory.json` records current source digest,
file count, dirty scope, Node and metadata-only credential candidates. Local SDK:
BE `.codex-tmp/sdk8-check/sdk/dotnet.exe` (.NET 8); Node 22.13.0, npm 11.1.0,
PostgreSQL 18. Record a new snapshot after source changes.

| Repo / log | Executed command / result |
| --- | --- |
| BE `backend-build.log` | isolated SDK `build viora-BE.sln -c Release --no-restore -m:1 -p:UseSharedCompilation=false`; exit 0 |
| BE `backend-final.log`, `backend-final.trx` | SDK `test Viora.Application.Tests/Viora.Application.Tests.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false --logger 'trx;LogFileName=backend-final.trx' --results-directory .codex-tmp/stage10`; **845 passed, 1 skipped, 0 failed** |
| BE `model-drift.log` | same test command with `--filter FullyQualifiedName~CurrentModelMatchesMigrationSnapshot`; **1 additional passed** after full suite; no DB access |
| BE `recovery.log`, `recovery.trx` | same SDK test with `--filter FullyQualifiedName~Stage10DatabaseRecovery`; actual drill passed; successful focused sample backup 309 ms/restore 910 ms |
| BE `readiness-red.log`, `readiness.log` | missing endpoint reproduced before implementation; focused `--filter FullyQualifiedName~PlatformReadiness` then **2 passed**, also included in full regression |
| BE `hosts-build.log`, `hosts-build-final.log` | Release builds of SsoE2EHost/DeveloperE2EHost, then updated resource-enabled SsoE2EHost; exit 0 |
| BE `confidential-sample-build.log` | SDK build actual docs `ConfidentialExample.csproj -c Release --no-restore -m:1 -p:UseSharedCompilation=false`; exit 0, not sample issuer E2E |
| BE `host-boundaries.log` | `node --test tools/MiniAppProductionE2E/production-partner.test.mjs tools/MiniAppReviewE2E/owned-browser-cleanup.test.mjs tools/ProductionHostTests/*.test.mjs tools/SandboxHostTests/*.test.mjs`; **40 passed** |
| BE `load-safety.log` | `node --test tools/MiniAppProductionE2E/load.test.mjs`; **1 passed**; arbitrary production targets rejected |
| BE `e2e-non-sso.log` | `node tools/MiniAppProductionE2E/run-fixture.mjs --non-sso-partner`; exit 0 |
| BE `e2e-public.log` | production runner `--runtime-security`; exit 0 |
| BE `e2e-confidential.log` | production runner `--confidential-partner --runtime-security`; exit 0 |
| BE `e2e-review-baseline.log`, `baseline.json` | production runner `--stage10-baseline --all-flows`; exit 0; all Public review decisions and six baseline shapes |
| BE `e2e-review-confidential.log` | production runner `--confidential-partner --all-flows`; exit 0 with current Release host and patched Vite |
| BE `inventory.json`, `inventory-summary.log` | `node tools/Stage10Audit/inventory.mjs`; exit 0; values never emitted |
| BE `nuget-audit.json` | SDK `list viora-BE.sln package --vulnerable --include-transitive --format json`; current advisory metadata, not cached-zero inference |
| FE `types.log`, `lint.log` | `node node_modules/typescript/bin/tsc --noEmit`; `npm run lint`; exit 0 |
| FE `developer-final.log` | `npm run test:developer`; **63 passed** after dependency patch |
| FE `docs-tests.log`, `docs-check-final.log` | `npm run test:docs` **14 passed**; `npm run docs:check` verifies 20 pages/54 operations/links/examples |
| FE `sdk.log` | `npm --prefix packages/miniapp-sdk test`; **37 passed** |
| FE `miniapp-wallet.log` | spec Node/tsx Mini App/Wallet/SSO command; **50 passed** |
| FE `production-export-final.log` | `node scripts/build-mini-app-deployment.cjs`, explicit fixture HTTPS origins, runtime enabled for Web, `EXPO_NO_DOTENV=1`, actual `EXPO_PUBLIC_MINI_APP_NATIVE_RUNTIME_ENABLED=false`; exit 0 |
| FE `portal-export.log`, `docs-build.log` | fresh Expo export to legacy stage7 directory, then `node scripts/build-developer-docs.cjs --base-export .codex-tmp/stage7-developer-final-export --output-dir .codex-tmp/stage9-docs-export`; exit 0 |
| FE `docs-e2e.log` | `npm run test:docs:e2e`; anonymous docs and actual local SMTP/account/Console browser flows exit 0 |
| FE `dependency-audit-final.json`, `sdk-dependency-audit.json` | fresh npm audit: FE **38** (32 High/6 Moderate), SDK **0**; FE audit exit 1 means advisories, not scan failure |
| Admin `build-final.log`, `lint-final.log`, `service-tests.log` | post-patch build/lint exit 0; **10 service tests passed**; Vite retains >500 kB chunk warning |
| Admin `dependency-audit-final.json` | fresh npm audit **0 advisories**, exit 0 |

Chrome DevTools MCP additionally navigated the actual local docs API Reference at
320 px/reduced motion: one main/h1, correct canonical/title, document width 305 px
(no page overflow). Anonymous account decoration returns expected 401; no other
console warning/error was observed in that check. Fresh E2E screenshots cover
320/375/768/1440 light/dark; the 320 light screenshot was visually inspected.
MCP file output was denied by its workspace-root policy; inline observation was
used instead. No credentials/storage were inspected.

## Failed attempts and coverage limits

- Sandbox process permissions caused initial Node/Vite `spawn EPERM` and PG
  startup failures. Authorized runs outside sandbox separated environment from
  product failures; failures were retained, not counted as passed checks.
- Unbounded xUnit class concurrency then produced three owned `initdb` timeouts.
  Source-level concurrency limit resolved them. One intermediate recovery build
  collided with a running test DLL; subsequent builds were sequenced.
- Recovery tooling initially passed Npgsql syntax to libpq; explicit host/port/
  username/database flags fixed it. PG18 deparse differences were diagnosed and
  canonicalized through its own parser; integrity checks were retained.
- A Confidential all-flows attempt selected cached Debug host output and failed
  readiness; `e2e-review-confidential-debug-unaccepted.log` is **not acceptance
  evidence**. Current review verification uses freshly built Release output.
- The separate MoneyAudit concurrency test remains skipped because
  `VIORA_MONEY_AUDIT_TEST_DB` was not configured. No live DB was substituted.
- Auxiliary missing Social controller DI and canceled teardown requests are
  disclosed in the [E2E assertion map](../testing/developer-platform-stage-10-e2e.md).
- No staging/VPS checks, actual Mac/iOS run, accepted Android permission/SSO run,
  production-size/key restore, alert delivery, Docker image inspection or direct
  standalone sample issuer E2E was performed. These remain release gates.

## Follow-up order and release decision

1. Close direct standalone Public/.NET sample issuer/browser coverage with owned
   trusted TLS adapters; do not weaken sample TLS/origin/nonce validation.
2. Resolve remaining advisory reachability/fixes, inspect a clean container image,
   and provision owned staging with confirmed operators/configuration.
3. Compose docs/security policy in the actual release artifact, prove trusted
   proxy/HTTPS issuer behavior, recovery/rollback compatibility and alert delivery.
4. Resolve native trust P0 before device acceptance; get Mac/iOS access separately.
5. Review production SLOs, enforce release approvals and sign a new GO/NO-GO.

See [security](../testing/developer-platform-stage-10-security.md),
[DB/recovery](../testing/developer-platform-stage-10-database.md),
[performance](../testing/developer-platform-stage-10-performance.md),
[release checklist](../releases/miniapp-platform-production-readiness.md) and
[runbook](../runbooks/miniapp-platform-deployment.md). Documentation completion
and local green tests do not close missing mandatory acceptance evidence.
