# Stage 6 acceptance evidence

Final local acceptance, 2026-10-20. All 32 implementation tasks are complete within
the approved web/controlled-fixture scope. Production rollout and its deployed
header/DNS/TLS checks remain separate. See the canonical
[plan](../plans/developer-platform-stage-6.md) and [runbook](../operations/developer-platform-stage-6.md).

## Delivery categories

| # | Category | Evidence / remaining work |
| --- | --- | --- |
| 1 | Architecture | Existing backend/DB, OpenIddict authority, Expo Portal/BFF; dedicated host; separate executor authority. |
| 2 | Schema | Sessions/grants/runs/results/diagnostics/protocol evidence; fixed catalogs/snapshots. Executor grants bind run/version. |
| 3 | Migrations | Latest `20261020065352_AddSandboxExecutorLaunchBinding`; isolated PG Up/Down/Up preserves owner grants. Down consumes outstanding executor grants. No application DB migrated. |
| 4 | APIs | Owned Developer/allowlisted Admin management; host-only runtime; independent executor. Optional run history `order=newest`; default order preserved. |
| 5 | Host | Real partner iframe, restrictive CSP/permissions, browser-bound one-use grants, heartbeat, reload/close and confirmation. |
| 6 | SDK bridge | Explicit v2, strict origin/source/schema/IDs; v1 preserved. Popup reuses PKCE/library validation; run hint is optional. |
| 7 | SSO | Actual HTTP correlation; Public/Confidential browser code/token/UserInfo; first deny then allow, preview reload and repeat consent fast path pass. Confidential repeat requires a new backend exchange. |
| 8 | Portal UI | Actual protected Expo route/BFF login; runs/create/cancel/results, readiness, filtered/paginated diagnostics and safe JSON export. |
| 9 | Diagnostics | Fixed catalog/source, quotas and retention. Host/SDK reports cannot become trusted proof. SSR malicious text is escaped. |
| 10 | Readiness | One newest completed run, current fingerprint/live source, matching persisted proof and final five-minute cutoff. All 18 checks passed in Public and Confidential Portal fixtures. |
| 11 | Security model | Independent auth contexts/key; run runtime cookie does not rotate/revoke owner preview. Independent review reconciled issuance/readiness races and runtime isolation. |
| 12 | Builds | Expo Web export, FE types/focused lint, SDK/fixture builds pass. .NET SDK 8.0.426 Release solution build: zero warnings/errors; EF model matches migration. Existing ReportReason sentinel warning is unrelated. |
| 13 | Unit tests | Backend 487 passed / 1 existing unrelated MoneyAudit skip; SDK 37; host/executor Node 23; FE/BFF 42. |
| 14 | Integration | Actual HTTP protocol/auth; isolated PG races, migration rollback and executor cookie isolation/revocation. |
| 15 | E2E | Real Developer/BFF/Portal/host/partner, Public/Confidential SSO and full readiness pass. Public authenticated external confirmation and SDK close pass. Controlled executor CSP refusal is Failed. |
| 16 | Incomplete work | No remaining local implementation task. Public deployment, application DB migration, real DNS/TLS, provider-deployed Vercel headers and native runtime were not performed. Browser policy limits below remain explicit. |
| 17 | Web/mobile limits | Real Portal viewport 320, 375×812, 768×1024, 1440 has no page overflow; keyboard launch/reload/stop and cancel/close focus pass. No native WebView claim. SameSite=Lax explicitly fails embedded authentication; COOP/storage-denial variants retain deterministic unit coverage, not blanket browser-policy acceptance. |
| 18 | Stage 7 prerequisites | Configure real origins, shared DP keys and independent executor/runtime; authorize schema rollout and verify effective deployed CSP/cookies. No deployment/publication/Admin Review/Stage 7 started. |

## Security mapping

Coverage statements below do not imply every browser policy was tested. Backend
suite names refer to `Viora.Application.Tests`; SDK/FE tests live in `../viora`.

| # | Threat | Executed coverage / limit |
| --- | --- | --- |
| 1 | Developer A accesses B | Session/endpoint/run/diagnostic ownership and BFF auth/allowlist tests. |
| 2 | Expired session | Fake-clock session/runtime/executor checks; actual Portal polling removes the expired preview and backend rejects new grants. |
| 3 | Revoked session | Session/runtime/retention/source-revocation tests; actual Portal stop. |
| 4 | Grant replay | PG concurrent redemption exactly one winner; one-use tests. |
| 5 | Cross-environment token | SSO environment/client/authorization regression and exact internal token provenance; Confidential lifecycle preserves nonempty Production registry/consent/token metadata, publication status, social refresh token and ANKT SSO session. Seeded preservation token is not a usable bearer credential. |
| 6 | Unverified domain | Session/config eligibility and live-proof revocation. Fixture ownership is seeded, not public DNS proof. |
| 7 | Suspended app | Session/source/config eligibility and protocol invalidation. |
| 8 | Arbitrary iframe URL | Registry snapshot, public URL/DNS policy and host checks; no arbitrary server JS/proxy. |
| 9 | Host spoof | Exact HTTPS authority, binding/nonce, endpoints and actual nesting. |
| 10 | Bridge spoof | SDK/host strict channel/version/schema/permissions; executor rejects child-origin contexts. |
| 11 | Invalid source | Portal transport/popup tests; exact frame source and CDP default main-world checks. |
| 12 | Invalid request ID | Replay/late/malformed/pending-disposal tests. |
| 13 | Unsupported method | v1/v2 capability and explicit UnsupportedMethod tests. |
| 14 | Malicious navigation | Final partner HTTP redirect to another origin is blocked and yields two TrustedBrowser Failed results. Unobservable same-origin internals are not trusted proof. |
| 15 | External URL | HTTPS/exact origin/public DNS/path/no query/fragment/auth destination; authenticated host confirms/cancels a real SDK request and new tab has neither opener nor referrer. |
| 16 | Consent bypass | Actual HTTP and Public/Confidential browser first deny, subsequent allow and repeat fast path. Consent label is server-selected Sandbox; denied attempt leaves no protocol proof. |
| 17 | Token leakage | Internal IDs only, bounded DTO/catalog, partner-only popup/memory, export allowlist, filtered browser env. No arbitrary console/credential scraping. |
| 18 | CSRF | BFF origin/header, host Origin/cookie binding and independent internal auth. |
| 19 | XSS | React SSR injection regression, host textContent, fixed messages, consent escaping/CSP tests. |
| 20 | Unauthorized diagnostics | Owner/source tests; Developer/Admin/bridge cannot authenticate executor or stamp trusted source. |
| 21 | Oversized logs | Body/batch/event/session caps, dropped counters, query caps and UI 200-event bound. |
| 22 | Rate limit | Actor/IP/session tests, queue/active-run caps, bounded browser jobs/frames. |
| 23 | CSP blocked iframe | Final real frame-ancestors refusal produces two TrustedBrowser Failed results, never iframe-load Passed. |
| 24 | Callback mismatch | Actual library redirect/state/nonce/issuer/audience/signature and popup origin/source tests. |
| 25 | SDK timeout | Final delayed real SDK handshake exceeds the bounded browser window and produces two TrustedBrowser Failed results. Deterministic popup/bridge tests cover cleanup and late replies. |

## E2E reconciliation

| Steps | Evidence |
| --- | --- |
| 1–7 | Actual Developer login, protected route/config, session/grant/host/bridge pass. Cross-owner denial covered HTTP. |
| 8–11 | Actual Public/Confidential popup, first Sandbox consent deny/allow, code/token/UserInfo pass; server records all three protocol stages. |
| 12 | Public SDK tokens remain in memory. Confidential opaque cookie is revalidated by embedded /session and root UserInfo; no token/secret relayed through host. |
| 13–14 | Preview reload reacquires Public memory tokens; Confidential cookie revalidates. Both repeat without a consent click; Confidential requires a fresh backend exchange count. Separate SameSite=Lax browser reports embedded session unavailable despite real top-level code/token/UserInfo success. |
| 15 | Actual environment/inside APIs; authenticated SDK external cancel/confirm with no opener/referrer, SDK close/revocation/focus and reopen. Unsupported methods covered by host/SDK tests. |
| 16–17 | Actual diagnostics, cancel/focus and all 18 checks/fresh review readiness pass for Public/Confidential. |
| 18–19 | Actual keyboard Portal stop restores focus; stopped/expired sessions reject grant issuance. Expired preview is removed by normal polling. |
| 20 | Both lifecycles preserve nonempty Production client/consent/library-token metadata and publication plus social/ANKT sessions. ANKT snapshot precedes reload/SDK close. Final audit found no task issuer/browser process or executor profile; sample UI server/tab and six verified abandoned task directories removed. Unidentified older directories preserved. |

## Final verification artifacts

Local artifacts are deliberately outside delivery source; never commit fixture
keys, generated DBs, exported bundles, browser profiles or test output.

- Backend: `.codex-tmp/stage6-final-regression.log`,
  `Viora.Application.Tests/TestResults/stage6-final-regression.trx`;
  `stage6-final-release.log`, `stage6-final-model.log`, `stage6-final-fixture-build.log`.
- Browser: `.codex-tmp/stage6-final-portal-public.log`,
  `stage6-final-portal-confidential.log`, `stage6-final-restricted-cookie.log`,
  `stage6-final-blocked-partner.log`, `stage6-final-redirect-partner.log`,
  `stage6-final-delayed-partner.log`. All six commands exit successfully; negative
  runs assert Failed/unavailable rather than interpreting process exit as proof.
- Node host/executor: `.codex-tmp/stage6-final-host-executor.log`.
  FE `.codex-tmp/stage6-final-tests.log`, `stage6-final-sdk.log`,
  `stage6-final-types.log`, `stage6-final-lint.log`, `stage6-final-export.log`.
- Screenshots: `.codex-tmp/stage6-portal-screenshots/{Public,Confidential}-{375,768,1440}.png`.
  Mobile Public and tablet Confidential screenshots visually inspected.
- Independent read-only review reconciled run issuance/readiness races, restricted
  FK retention, runtime credential separation, focus timing, repeat-SSO false
  positives and signal termination falsely reporting success. Required findings
  fixed; no outstanding implementation finding. Existing dirty changes preserved;
  no commit, application DB migration, deployment or publication.

## Reproduction and trust boundary

See [operations](../operations/developer-platform-stage-6.md). The controlled
executor replaces only its exact main-frame Portal document after a real TLS
response with a fixed credential-free launcher. Real host/partner content, CSP,
permissions, handshake and CDP frame checks remain intact. This proves runtime/
bridge, not actual Portal authentication or deployed CSP. `portal-e2e.mjs`
separately loads the real exported Portal/BFF without document replacement. Its
local header adapter is not evidence of Vercel's deployed route matching.

Local aliases map to disposable loopback servers. Only the fixed executor fixture
Portal origin receives `loopbackNetwork` in its fresh profile; production receives
no grant. See the [official CDP Browser protocol](https://chromedevtools.github.io/devtools-protocol/tot/Browser/).
TLS exceptions pin the fixture leaf SPKI; system trust/user profiles are unchanged.
