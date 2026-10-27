# ANKT Developer Platform — Stage 6 implementation plan

Status: APPROVED by the user's instruction to begin implementation, 2026-10-17.
[Specification](../specs/developer-platform-stage-6.md). Pre-flight complete;
Tasks 1–32 implemented and locally verified, 2026-10-20. Final
[18-category acceptance, 25-threat mapping and 20-step E2E reconciliation](../handoffs/developer-platform-stage-6-acceptance.md)
supersedes the historical checkpoints below. Public/Confidential authenticated
Portal/SSO, restricted-cookie failure and CSP/navigation/SDK-delay negatives pass.
Stage 6 is complete within the approved web/controlled-fixture scope. Deployed
headers, public DNS/TLS, application DB rollout and native runtime remain separate;
no deployment, publication or Stage 7 work was performed.

## Gate and verified baseline

- BE `d57276ba`; FE `4c8a62f`. Stage 5 spec, all 28 planned tasks and final handoff
  audited against source; reuse its server/registry/SDK/bridge and Expo BFF.
- This pre-flight: backend 329 passed/1 pre-existing MoneyAudit skip; Developer/BFF
  27 + bootstrap 7 passed; SDK 23 passed/build; existing HTTPS Public/Confidential
  E2E 1 passed; SsoE2EHost build 0 warnings/errors. Backend TRX:
  `.codex-tmp/test-results/stage6-preflight.trx`.
- Existing fixture exercises actual discovery/JWKS/code/token/consent/UserInfo;
  seeded domain/user setup is isolated. No public-domain/native/deployed check.
- Missing Stage 6 pieces: session/grant/run/diagnostic schema, runtime authorization,
  isolated host route/origin, scoped CSP, new bridge methods, embedded popup login,
  consent environment label, trusted runtime evidence, readiness and Sandbox UI.
- Resolve the four spec approval questions before implementation: host origin,
  popup/cookie limits, Admin/limits policy, trusted executor authority. A rejected
  choice changes affected tasks before code starts. No mock SSO fallback.

## Execution rules and dependency order

Continuation 2026-10-20, Tasks 25–28: bind the non-authenticating `sandbox_run`
marker to a live run/client/session snapshot inside a server-generated code's
private claims. Observe generated internal code/token IDs after OpenIddict token
generation; correlate validated UserInfo by that exact internal token ID. Optional
observations use separate DI scopes and cannot change ordinary OIDC outcomes.
Trusted evidence uses a separate disabled-by-default executor key, protected
per-run/check/version/session/fingerprint/expiry challenges and signed fixed
outcomes. Persist server-generated challenge IDs in existing diagnostics to reject
replays, with serializable per-check single ingestion. A later integration fix
adds nullable executor run/version binding to launch grants; a purpose-specific
protected runtime cookie expires with that run and cannot rotate/close the owner's
preview credential. No owner result-writing endpoint. The worker consumes only server protocol/trusted browser
proof; readiness uses fresh completed runs with matching current fingerprints.

Task 20 continuation: add an owner/Admin-bound `GET /sandbox/readiness` without
requiring a launched session. Evaluate the ten existing metadata/legal checks
plus explicit Sandbox environment, client and actual discovery/JWKS. Reuse public
URL validation and the pinned credential-free fetch transport; JSON has its own
strict media mode. Reuse Stage 5 SSO registry eligibility/projection checks.
Recheck actor, configuration fingerprint and expiring proof after network I/O.
Callbacks/discovery/client/protocol are inapplicable only when persisted SSO is
disabled. Launch gates are website/domain/environment and applicable SSO gates;
legal/support/metadata additionally gate review. Runtime/bridge/protocol remain
MissingEvidence until their dedicated tasks; readyForReview remains false.
Expose fixed catalog codes/provenance, never fetched content or credentials.
Wire the same checker into persisted runs; preserve the legacy readiness API.

Task 19 continuation: use the persisted run table as the bounded queue (max 64
queued/running runs globally, one active run/session), not an in-memory work queue.
Runs have a server-selected 120-second deadline capped by session expiry. A
serializable claim changes Queued to Running with a new concurrency version; only
that version may write results or complete. Cancel/revoke/expiry invalidate it.
A bounded process-local cancellation registry interrupts local work; other
instances also enforce persisted status/version/deadline before committing.
Restart does not replay Running jobs or cancel another instance's live work:
abandoned Running jobs fail at their persisted deadline and queued jobs survive.
The worker reconciles expired rows before claiming, uses one scoped execution at
a time and safe catalog results only. Until Task 20, the default executor records
NotImplemented/MissingEvidence, never fake Passed. Completed means processing has
ended, not that checks passed. Add owned GET/list and DELETE cancellation routes;
POST accepts session/check keys only. Check/source/status cannot be submitted.

Continuation scheduling (2026-10-19): browser access is still unavailable. Build
Tasks 15–18's independent persistence/diagnostic/retention foundation while keeping
Tasks 9–14 browser acceptance open. Do not publish runtime readiness or treat that
acceptance as passed. This does not change the approved security boundaries.
Task 15 stores run snapshots under the session, unique check results, fixed-code
diagnostics and internal protocol evidence. Diagnostics reference session/run
with a composite FK; app/owner are derived through the session, not caller fields.
Run/check identifiers and status/source/message catalogs are enums with database
constraints; only trusted sources may persist Passed, runtime/bridge require
TrustedBrowser and SSO protocol checks require ServerProtocol. One queued/running
run per session is enforced by a filtered unique index. All new FKs restrict
deletion; bounded cleanup explicitly deletes dependents first. Task 16 generates
and verifies a separate additive migration on isolated PostgreSQL; no configured
application DB access. Tests cover cross-session binding, uniqueness, bounds,
invalid enums, untrusted Passed and unchanged identity/consent/client data.
Task 17 stores a saturating dropped-event counter on the session (separate
additive migration after Task 16), validates an entire batch before writing, and
uses a serializable transaction for persisted per-minute/session quotas. Runtime
observations are stamped SDK/Host by separate server routes, never a request field;
owner/Admin reads resolve through existing access/session services. No endpoint
accepts a result status or trusted evidence. Pagination is bounded and opaque;
unknown JSON fields/duplicates, excessive depth/body/batch and inactive session/run
are rejected. The bridge/Portal presentation remains later integration work.

Work sequentially in thin slices. Each implementation task starts with a focused
failing behavior test, then minimum code, build/relevant tests, diff/security review
and evidence. Validate an existing capability rather than writing mirror tests.
The file sets below are targets, not permission for unrelated edits; split a task
if more than about five authored files are required. Migration generated designer
and snapshot are isolated in dedicated tasks. Preserve existing unrelated FE edits.

Graph: approval → access/config → sessions/grants → isolated host/transport → UI;
run/evidence storage → diagnostics/coordinator → real OIDC observations/executor →
readiness → complete UI/E2E/security/report. Popup feasibility is checked before
large UI work. Serious origin/auth/contract changes return to spec review; routine
approved work needs no repeated permission. No parallel implementation streams.

## Tasks

Paths: BE is repository root; FE is `../viora`; SDK is
`../viora/packages/miniapp-sdk`. Names below are proposed and may follow existing
conventions without changing the approved boundaries.

| # | Slice and acceptance | Dependencies | Authored files (target ≤5) | Required verification |
| --- | --- | --- | --- | --- |
| 1 | Disabled-by-default Sandbox options and typed actor access. Distinct host/Portal/issuer origins; owner vs explicit Admin role+allowlist; teams denied. | Approval | BE options, access policy, actor DTO, policy tests (4) | Fail invalid/missing/equal origins, inactive owner, other owner, revoked source session, non-allowlisted Admin; default auth schemes unchanged. |
| 2 | Session/launch-grant domain and EF mappings: owner/actual actor, source session, Sandbox constraint, hashes, snapshot/version, expiry indexes. | 1 | Entities, configuration, AppDbContext, persistence tests (4) | Model constraints/FKs/unique hashes; no raw grant/secret storage; Admin reference is not a Developer FK. |
| 3 | Additive session/grant migration only. | 2 | Migration, designer, snapshot (3 generated) | Review generated PostgreSQL SQL; isolated PG up/down/up, uniqueness, preservation of unrelated records and rollback. |
| 4 | Session lifecycle service: create/get/revoke, app/client/proof gates, 15-minute default/30-minute cap, active-session quota, stale snapshot denial. | 3 | Service, lifecycle/access helper, DTO, service tests (4) | Deterministic expiry; A/B ownership; no-SSO preview; suspended/deleted/rejected app; revoked Developer session/client/proof; competing creates/close on PG. |
| 5 | Developer session-management endpoints and separate Admin test routes; consistent errors, rate limits, audit and actor derivation. | 4 | Developer endpoints, Admin endpoints, registration, HTTP tests (4) | Authentication/authorization per method; IDs cannot cross app/session/actor; request cannot choose owner/Production/expiry beyond limits; no publication mutation. |
| 6 | Browser prepare/binding, launch grant issuance/redemption and atomic one-use consumption; no credentials in navigation. | 5 | Grant service, runtime endpoints, management endpoint extension, HTTP/PG tests (4–5) | Cookie+nonce+audience+source-session binding, forged Origin, replay/race, expiry, wrong app/host and transaction rollback. |
| 7 | Dedicated host authority/route/static assets and startup controls in existing API, not another server product. | 6 | Host endpoints, host document, registration/options, host HTTP tests (4–5) | Wrong Host/proxy attribution denied; no management/auth routes on host surface; cookie isolation; generic shell leaks no app data before redemption. |
| 8 | Early real-browser feasibility fixture for nested host, exact CSP, popup/COOP and storage/cookie restrictions. Confirm approved SSO design before expanding UI. | 7 | Existing E2E host extension, partner fixture extension, browser test, test launcher (4) | Chromium actual nested frames; same-site host cookie; partner cookie blocked mode; popup opener behavior; iframe-CSP block. If design fails, stop and revise spec, never replace with mock success. |
| 9 | Bridge v2 negotiation/schema/types while preserving v1. Add environment/inside-host APIs and typed UnsupportedMethod, bounded observations. | 8 | SDK bridge, protocol types, exports, bridge tests (4) | Exact source/origin/version/random IDs, unknown methods, malformed/oversize messages, stale replies, timeout, pending cap, disposal and v1 compatibility. |
| 10 | Real host partner iframe lifecycle and host header. Registry-sourced URL, restrictive permissions, heartbeat/liveness, connection timeout. | 9 | Host script, host document/style, runtime context service, browser/HTTP tests (4–5) | Real verified fixture loads/handshakes; arbitrary URL and privileged origins denied; stop removes iframe; no load-event-only Passed or cross-origin DOM access. |
| 11 | Extend existing BFF allowlist and typed FE Sandbox client. No passthrough credentials/admin/runner routes. | 10 | FE account proxy, Sandbox client/types, BFF/client tests (4) | Exact methods/paths, CSRF/origin, size/timeout/redaction, credentials omitted from errors, cross-owner denial and source-session expiration. |
| 12 | Protected Expo Sandbox route, app-detail entry, shell/header/toolbar and empty/loading/error states using existing Console layout. | 11 | FE route map, app detail, Sandbox screen, CSS, route tests (5) | Only `/developers/console/apps/{appId}/sandbox`; unauthenticated/other owner/stale app; no second root Portal/Next.js app or native claim. |
| 13 | Portal↔host launch handshake and scoped deployment CSP. Host-ready source/origin check, POST grant, postMessage redemption and liveness. | 12 | FE transport helper, screen, route-header configuration, transport tests, header tests (5) | No URL credential; wrong source/origin/nonce, replay, blocked cookie/iframe, expired grant. Actual headers permit host only on Sandbox route; other Portal routes keep frame-src none. |
| 14 | Stop/reload/back/close lifecycle, heartbeat and navigation cleanup; visible retry on failed revoke, expiry fallback. | 13 | Host lifecycle, FE transport/screen, lifecycle tests (4–5) | Concurrent/duplicate close is safe; reload needs new connection/grant as appropriate; no arbitrary back destination; ANKT/partner/Production sessions unchanged. |
| 15 | Runs/results/diagnostics/protocol-evidence domain and configuration. Fixed status/catalog/provenance, snapshot binding and uniqueness. | 14 | Domain records, EF mappings, AppDbContext, persistence tests (4) | Bounded fields, run/session/app FKs, enum checks, unique run/test key, internal token IDs only; no raw credential or arbitrary text fields. |
| 16 | Separate additive run/diagnostic/evidence migration. | 15 | Migration, designer, snapshot (3 generated) | Reviewed SQL and isolated PG up/down/up, rollback, indexes and preservation of sessions/OIDC/identity/consent. |
| 17 | Diagnostic catalog, safe typed ingestion/query and quotas. Separate server, host, SDK and trusted-runner provenance. | 16 | Catalog/DTO, service, endpoints, tests (4–5) | Reject extra fields/token-like arbitrary text, oversized batches/quotas; cross-owner/admin isolation; pagination cap; bounded drop counters; encoded rendering inputs. |
| 18 | Expiry/retention cleanup worker: revoke expired work, purge diagnostics after 7 days and closed summaries after 30, bounded batch. | 17 | Worker, service retention helper, registration, tests (4) | Fake-clock boundaries, repeat execution, active record preservation, relational delete ordering and no deletion of consent/identity/production data. |
| 19 | Bounded test-run coordinator/worker: one active run/session, queue cap, cancellation/deadline and persisted transitions. | 18 | Coordinator, worker, endpoints, registration, tests (5) | Concurrent create/stop, expired session, restart reconciliation, cancellation, no client-selected result/status or arbitrary server-side code. |
| 20 | Configuration checks and launch readiness reuse registry/domain/SSO services. All 11 readiness keys represented with applicability. | 19 | Check catalog, checker, readiness DTO/service, tests (4–5) | Metadata/legal/support/domain/HTTPS/client/callback gates; SSO disabled handling; config-only checks never imply protocol/runtime Passed. |
| 21 | Implement v2 getEnvironment/isInsideAnkt/close/openExternalUrl in real host and SDK, with confirmation and permission enforcement. | 20 | SDK bridge, host script, URL policy, SDK tests, browser tests (5) | Environment cannot be Production; exact allowlist and public HTTPS; reject private/auth/query/fragment URLs; user confirmation, noopener/noreferrer; unsupported capabilities fail explicitly. |
| 22 | Opt-in SDK popup login transport under the approved feasibility result; reuse Stage 5 PKCE/library validation and existing top-level login behavior. | 8,21 | SDK popup adapter, auth integration, callback helper, auth tests, public API types (5) | Real known popup source and exact origin; state/nonce/signature checks; blocked popup, timeout, logout race, navigation cleanup, storage denial and COOP failure. No token/code goes through host bridge. |
| 23 | Public/confidential partner examples implement callback completion for popup. Tokens stay partner-side; confidential backend owns session. | 22 | Public example, confidential example, callback sample, example integration tests (4–5) | Real code exchange/UserInfo for both; iframe session revalidated; same-origin callback injection denied; cookie-blocked top-level success is not labelled embedded success. |
| 24 | Server-bound Sandbox environment label on existing consent page/JSON model. | 23 | Consent page model, controller, Razor view, consent tests (4) | Query cannot choose environment; correct Sandbox label/name/logo/developer/scopes; first/repeat/forced/deny unchanged; Production label correct. |
| 25 | Correlate active Sandbox runs with actual existing authorize/token/UserInfo events and internal IDs. No new authorization authority. | 24 | Evidence service, authorize hook, token/UserInfo hook, contracts/registration, protocol tests (5) | Tampered/missing/stale run or wrong client/environment cannot manufacture evidence; valid normal OIDC requests unchanged; no raw URL/code/token logs; actual protocol outcomes required. |
| 26 | Trusted browser-executor authentication/evidence contract. Distinct server authority; per-run/check/session/config binding and replay/expiry checks. | 25 | Executor auth/options, evidence validator, internal endpoint, executor HTTP tests (4–5) | Developer/Admin/bridge cannot submit trusted Passed; unauthenticated or reused/stale/other-session evidence rejected; disabled executor yields explicit missing proof. |
| 27 | Controlled real-browser executor and cooperative partner fixture. Reuse current test tooling; pin any needed dependency before adoption. | 26 | Browser executor/launcher, partner fixture, existing E2E host extension, executor tests (4–5) | Real navigation/handshake/SSO observations; signed authenticated result ingestion; CSP-blocked, malicious navigation, timeout and cookie restrictions; never scrape partner secrets into results. |
| 28 | Aggregate readiness with freshness/fingerprint/provenance. Runtime/bridge/protocol evidence must satisfy required server rules. | 27 | Readiness service, DTO/catalog extension, readiness HTTP/unit tests (3–4) | No developer self-Passed; stale/failed/skipped required evidence blocks review; runtime unavailable blocks only the relevant stage; flags never mutate publication or Admin approval. |
| 29 | Diagnostics, test-run controls/results and readiness panels in existing Sandbox screen, including retry/cancel and empty/error. | 28 | Diagnostics panel, checks panel, screen integration, UI tests (4–5) | Status/provenance/remediation displayed; logs bounded/paginated; malicious content escaped; loading/timeout/unauthorized/session expiry and test cancellation are actionable. |
| 30 | Device switching and accessibility/responsive completion. | 29 | Preview component, Sandbox styles, keyboard helpers if needed, UI/browser tests (4–5) | 375×812, 768×1024, desktop; 320px Portal/no page overflow, visible frame scroll/fit; keyboard launch/stop/logs/close, focus restoration and announcements. |
| 31 | Complete requested end-to-end sequence with real Developer login, public/confidential partner variants and negative browser paths. | 30 | E2E scenarios, controlled host/partner fixture extensions, executable test script (≤5) | Execute 20 steps below; first/repeat/deny, revocation, failed embedding and Production isolation; exact executable commands and results recorded. |
| 32 | Full security regression mapping, review and final 18-category handoff/runbook. | 31 | Security tests/report, operations guide, handoff, spec/plan status (split into test and docs increments if >5) | All 25 threats below mapped honestly, clean builds/types/relevant suites, PG migration/races and real browser evidence; docs links/commands checked; incomplete items remain explicit. |

## Checkpoints

After 3, 6, 8, 10, 14, 16, 20, 24, 28 and 32: inspect diff and tests first;
build impacted project, run affected regressions, check secrets/permissions/expiry
and record evidence/limits. User may review checkpoints; only a substantial spec
  change requires renewed approval after the initial gate. Checkpoint 8 is an
architecture feasibility gate: failure blocks popup/runtime-dependent tasks.

Full backend build/tests at schema/protocol/final checkpoints; SDK test/build after
SDK changes; FE developer/bootstrap tests, TypeScript, focused lint and production
web export after UI/configuration changes. Use actual browser runtime to verify
effective CSP, cookie/origin isolation and lifecycle, not component snapshots alone.
Do not repeat a successful command without changed code or an unresolved concern.

Existing exact commands are in the spec. New focused Sandbox suites and a real
browser executor launcher must be implemented before documenting them as runnable.
No test result below is claimed before execution. No production service is started
by verification. Fixtures must stop and their verified temporary storage be removed.

## Security acceptance matrix (25 required cases)

| # | Requested attack / negative case | Tasks / required evidence |
| --- | --- | --- |
| 1 | Developer A accesses Sandbox B | 1,4–6,11: API/BFF/service and session/run/diagnostic combinations. |
| 2 | Expired session | 4,6,10,18: deterministic clock plus browser expired UI. |
| 3 | Revoked session | 4,6,14,19: host heartbeat/context/run rejected after close. |
| 4 | Launch-grant replay | 6: real PostgreSQL concurrent redemption exactly one winner; browser re-send denied. |
| 5 | Cross-environment token | 20,25,28,31: actual Production token/resource cannot become Sandbox protocol proof; Production state unchanged. |
| 6 | Unverified domain | 4,10,20: no launch; loss of current proof terminates active eligibility. |
| 7 | Suspended Mini App | 4,5,10,25: all surfaces fail after lifecycle change. |
| 8 | Arbitrary iframe URL | 7,10,13: registry URL only; private/platform/unverified destinations denied. |
| 9 | Host-origin spoofing | 1,6–8,13: exact configured authority, parent origin and nonce/browser binding. |
| 10 | Bridge message spoofing | 9,21: channel/version/schema/permission attacks and bounded pending work. |
| 11 | Invalid source | 9,13,22: sibling/old iframe/unrelated popup rejected even at the same claimed origin. |
| 12 | Invalid request ID | 9,13,21: unknown/reused/late/malformed ID cannot settle or execute an action. |
| 13 | Unsupported method | 9,21: typed UnsupportedMethod, no side effect. |
| 14 | Malicious-domain navigation | 8,10,21,27: effective host frame-src/top-navigation constraints; unobservable same-origin internals explicitly Unknown. |
| 15 | External URL validation | 21: fixed allowlist, HTTPS/public-network, no credential/query/fragment/auth destination; confirmation; approved popup exceptions documented separately. |
| 16 | Consent bypass | 22–25: actual ANKT user required; owner/Admin is not user consent; first/deny/repeat plus environment label. |
| 17 | Token leakage | 6,9,17,22–27: no credentials in URL/host bridge/diagnostics/logs or trusted-runner results; callback stays within partner channel. |
| 18 | CSRF | 5,6,11,13,26: management mutations, runtime binding/cookie, cross-origin and internal runner boundaries. |
| 19 | XSS | 10,17,24,29: escaped names/messages, code catalog instead of raw errors, no HTML injection, effective CSP. |
| 20 | Unauthorized diagnostics | 5,17,26,29: owner/admin scope, source session, no Developer trusted-result privilege. |
| 21 | Oversized logs | 17,18,29: byte/batch/event/retention/pagination caps and bounded failure. |
| 22 | Rate limit | 5,6,17,19: actor/IP/session quotas exercised, 429 and no unbounded background queue. |
| 23 | Iframe blocked by CSP | 8,10,27,31: real blocked fixture and unresolved timeout UI; never claim load success or bypass headers. |
| 24 | SSO callback mismatch | 22–25: real library exact binding, state/source/callback origin mismatch, no code exchange success. |
| 25 | SDK timeout | 9,13,22,29: deterministic unit timeout plus controlled browser delay; cleanup/no stale state restoration. |

Additional release checks: popup-blocked/COOP severed opener, partitioned partner
cookies/storage, stale configuration evidence, disabled executor, actor logout,
Admin allowlist removal, out-of-order/replayed evidence and backend restart.
Normal Bearer replay while valid remains the Stage 5 documented boundary.

## Requested real fixture E2E (20 steps)

1. Login through actual Developer Auth/BFF with isolated test identities.
2. Open owned Mini App detail; prove another owner is denied separately.
3. Enter protected integrated Sandbox route.
4. Inspect live configuration/readiness and current version fingerprint.
5. Create Sandbox session; no credential in navigation or page metadata.
6. Redeem host-bound launch grant and load the real controlled partner website.
7. Complete real SDK bridge handshake; matching app and Sandbox environment.
8. Start actual Stage 5 OIDC through the approved top-level popup path.
9. First consent shows app/developer/scopes and Sandbox; exercise deny separately.
10. Real code exchange validates PKCE/state/nonce/issuer/audience/signature.
11. Real UserInfo succeeds only for the matching valid Sandbox authorization.
12. Confidential partner creates its separate protected session; Public SDK keeps
    its own tokens in memory. Never call Public memory state a backend cookie.
13. Reload preview.
14. Verify real partner session or reacquire Public authorization; repeat consent
    fast path only when still valid; restricted-cookie mode must fail honestly.
15. Execute implemented environment/inside/close/external methods; unsupported
    requests rejected. Reopen a new session after testing close where needed.
16. Inspect redacted host/SDK/backend diagnostics and their evidence provenance.
17. Run checks; verify live protocol/runtime results and review readiness freshness.
18. Close Sandbox through host/Portal control.
19. Confirm revoked session/grants reject further host actions and cancel work.
20. Confirm Production registry/consent/tokens/publication and ANKT user session
    are unchanged. Stop fixtures and remove only verified temporary artifacts.

## Risks, exit criteria and delivery

- Nested-frame ancestors and existing Portal frame-src need actual effective-header
  tests. Partner refusal to embed is a supported error, not permission to proxy it.
- Popup permission cannot constrain every partner-created popup; approval must
  acknowledge that limit or revise the design before Task 8/22.
- Browser cookie/storage policies may prevent embedded partner session reuse.
  Top-level success is separate evidence and cannot force embedded readiness.
- Developer/browser observations are forgeable. Trusted execution authority and
  replay-bound proof must exist before required runtime checks can assert Passed.
- Admin/owner access and data retention must not become implicit publishing power;
  shared DB work must preserve Stage 5 identity and Production environment.

Completion requires real host/UI, approved SSO behavior, executed tests/security/
PostgreSQL/browser evidence, accurate remaining limitations and the final handoff:
architecture, schema, migrations, APIs, host, SDK bridge, SSO, Portal UI, diagnostics,
readiness, security model, builds, unit/integration/E2E, incomplete work, web/mobile
limits and prerequisites for a separately approved Stage 7. No auto deployment,
package publication, production migration, Admin Review or Stage 7 work.

## Checkpoint 8 — 2026-10-17

Tasks 1–7: disabled-by-default options/distinct host validation, typed Developer/
Admin actor access, live source-session and current registry/domain checks; shared
Sandbox session/grant schema and additive `20261017071740_AddMiniAppSandboxSessions`;
absolute expiry, quotas, configuration snapshot, owner/Admin management APIs;
browser-binding preparation, hashed one-use grant, host-only cookie runtime and
isolated host shell. The shell verifies sessions but does not yet load a partner
or provide the Stage 6 bridge/UI; that is Task 10 onward, not a completion claim.

Real PostgreSQL tests cover migration up/down/up, preserved registry, check/FK
constraints, rollback, session concurrency/quota and exactly-one grant redemption.
An observed Npgsql nested serialization failure now returns structured 409 after
transaction disposal without retry; deterministic interceptor regression plus real
PG race pass. Full backend 351 passed/1 existing MoneyAudit skip; targeted Sandbox
22 passed, clean solution build and no pending EF model changes. Existing EF
ReportReason sentinel design warning is unrelated and preserved.
TRX: `.codex-tmp/test-results/stage6-checkpoint7-full.trx` and
`stage6-checkpoint7.trx`. No configured application DB was accessed/migrated.

Task 8 real Chrome feasibility uses opt-in existing SsoE2EHost
`--sandbox-feasibility`, distinct `portal.ankt.localhost:5494` and
`sandbox.ankt.localhost:5493`, and existing Public/Confidential HTTPS examples.
Browser confirms host cookie available and Portal cookie absent at host. Nested
Public iframe performs actual PKCE/library exchange/UserInfo via known-popup,
same-origin partner callback, first/repeat consent; tokens never enter host bridge.
Blocked-popup variant reports popup_blocked. Actual COOP-severed callback times out.
CSP-denying fixture remains unconnected, never Passed. Confidential iframe receives
its real server session with explicit Secure SameSite=None sample mode; a separate
SameSite=Lax negative run succeeds top-level but remains unauthenticated embedded.
This last test exercises SameSite restrictions, not a claim of global browser
third-party-cookie blocking coverage. Public positive run has no console warnings/
errors; intentional blocked paths emit expected browser policy diagnostics.

New controlled example framing is off by default and only allows exactly two
configured local HTTPS ancestors; hostile/default framing tests pass. SDK total
24 unit tests/build pass; FE TypeScript passes. The popup prototype is confined to
fixture examples; production SDK popup adapter is still Task 22. The real host
fixture UI is a browser-policy experiment, not authenticated Developer Portal E2E.
Public-domain/native/proxy rollout remain unverified. No deployment, publication
or commit; unrelated FE notification/theme edits preserved.

## Continuation checkpoint — 2026-10-19

Audited existing work beyond the handoff: Tasks 9–14 have SDK v2, isolated host
partner lifecycle, owner BFF/client, protected Expo Sandbox screen and transport.
Do not mark browser-dependent acceptance complete: no usable browser connection
was available (Chrome DevTools profile busy; Browser runtime list empty).

This continuation fixes concurrent/replayed close handling, request replay across
navigation, own-envelope validation, SDK close disposal, immediate partner removal
during host revoke, late initialization after stop and handshake timeout reset.
New regression tests first reproduced concurrent close, inherited envelope, stale
replay, delayed stop/navigation timeout and SDK disposal failures, then passed.

Fixed Portal deployment frame-src with an optional exact host origin on the
Sandbox route and fresh document navigation on entry/exit. `../viora/vercel.cjs`
replaces root vercel.json; unchanged base headers/rewrites reside in
`../viora/server/developer/vercel-base.json`. Build env
`ANKT_SANDBOX_HOST_ORIGIN` must match BE; Portal/issuer HTTPS origins are explicit,
and hostnames must differ. Blank keeps all framing denied. Actual Vercel routing
and effective browser headers remain unverified; JS regex unit tests alone do not
prove platform routing behavior. No deployment or new dependency was introduced.

Executed: backend full 357 passed / 1 existing skip; focused Sandbox 28 passed;
final host endpoint rebuild/test 2 passed; final backend build 0 warnings/errors;
SDK 28 passed/build; Node host 12 passed; Portal/BFF 33 passed; FE TypeScript,
focused ESLint and final Expo Web export passed. git diff whitespace checks pass.
Host VM tests are logic-only doubles, not real iframe/CSP/cookie evidence.
Logs: BE `.codex-tmp/stage6-continuation-tests.log`, `stage6-host-tests.log`;
FE `.codex-tmp/stage6-portal-tests.log`, `stage6-sdk-tests.log`,
`stage6-checkpoint-web-build.log`. Windows sandbox blocked child-process spawning;
Node isolation was disabled and approved unsandboxed build/test runs completed.

Next checkpoint: execute Tasks 9–14 browser/Portal/header acceptance, then Task 15.
Tasks 15–32, all final security-matrix/20-step E2E obligations, trusted runtime
readiness and production SDK popup transport remain pending. No application DB
migration, deployment, publication, commit or Stage 7. Both repositories' handoffs
now describe current code and these limits; unrelated existing edits preserved.

## Persistence, diagnostics and retention checkpoint — 2026-10-19

Built the independent Tasks 15–18 foundation while browser acceptance remains
open, per the continuation scheduling note above. No runtime proof was fabricated.

- Task 15: shared run/result/diagnostic/protocol-evidence entities with fixed enum
  catalogs, Sandbox-only environment, matching session fingerprint FK, unique
  run/check, one active run/session and restricted relationships. Diagnostic
  session/run is a composite FK; no caller-supplied app/owner. Database rejects
  browser-reported Passed; Runtime/Bridge require TrustedBrowser, and actual SSO
  checks require ServerProtocol. These constraints do not replace future executor
  authentication or actual protocol validation in Tasks 25–28.
- Task 16: additive `20261019080250_AddMiniAppSandboxEvidence`; disposable PG
  up/down/up, rollback, check/length constraints, exactly-one concurrent run and
  preservation of session/client/consent/identity verified. Session's added unique
  snapshot key contains its existing unique ID; existing rows cannot collide.
- Task 17: strict fixed-code ingestion (32 KiB, 20 events/batch, 64/minute,
  500/session), serializable persisted quotas and saturating dropped counter.
  Separate additive `20261019081045_AddMiniAppSandboxDiagnosticCounter` verified
  for safe default, bound, rollback and up/down/up. Whole invalid/over-quota batch
  rejected; no arbitrary text/token/HTML fields. Owned/Admin GET diagnostics uses
  current actor access and session binding, bounded category/cursor/limit query.
  Separate host-only SDK/host routes stamp source. v2 reportDiagnostic now crosses
  the actual host to SDK ingestion; host handshake reports HostReported separately.
  Busy diagnostics do not disable platform controls. BFF exposes GET only and
  preserves validated filters through direct and Vercel-rewritten routes. Typed
  FE client added; diagnostics panel remains Task 29.
- Task 18: opt-in periodic cleanup begins after one minute, each mutation batch
  <=100. Expired session credentials/grants and active runs are ended; 7-day
  diagnostics and 30-day closed runs/sessions purge in FK order over bounded
  sweeps. Active session data and user consent/identity/client rows preserved.
  Explicit stop cancels its active run in the same revocation transaction.
  Fake-clock boundary/batch/idempotence tests and real PG expiry/purge pass.

TDD covered missing entity/service/routes, untrusted evidence, duplicate active
runs/results, wrong snapshot/session, catalog and numeric bounds, unknown/nested
duplicate JSON, owner isolation, stale sessions, source injection and oversize.
Pagination originally lost rows at identical timestamps because SQLite stores UUID
text uppercase; a deterministic reproduction now verifies normalized case on both
sides and a real PG paginated query. PG race verifies diagnostic quotas cannot be
exceeded across processes; serialization conflicts return 409 without retry.

See [Stage 6 operations](../operations/developer-platform-stage-6.md) for exact
contracts, quotas/retention and enablement prerequisites. Full final verification
and remaining acceptance are recorded in the handoff. No configured application
DB was accessed/migrated, and no deploy, package publication, commit or Stage 7.

Final checks at this checkpoint: solution build 0 warnings/errors; full backend
393 passed / 1 existing skip; host Node 15 passed; FE/BFF 35 passed; TypeScript,
focused ESLint and Expo Web export passed; EF has no pending model changes.
Logs: BE `.codex-tmp/stage6-foundation-final-full-tests.log` and
`stage6-host-tests.log`; FE `.codex-tmp/stage6-foundation-portal-tests.log` and
`stage6-foundation-web-build.log`. Next approved backend task is 19. Browser,
actual Vercel header/Portal E2E and Tasks 19–32 remain open.

## Test-run coordinator checkpoint — 2026-10-19

Task 19 complete: persisted queue bounded at 64 globally and one active run/session;
120-second server deadline capped at session expiry; serializable version-bound
claim and guarded results/completion; owned/Admin create/list/detail/cancel APIs;
bounded cancellation registry; enabled-only sequential scoped worker. Stop commits
run cancellation before interrupting local work. Ignored cancellation and late
completion cannot restore a terminal run. Restart leaves another instance's live
Running job alone, fails abandoned jobs at deadline reconciliation and resumes
queued work. No arbitrary code, user-selected status or unbounded Task.Run queue.

No schema/migration added. Default server executor returns NotImplemented with
MissingEvidence until Task 20; Completed denotes finished processing only. BFF
exposes exactly POST/GET base and GET/DELETE run ID, with bounded list queries
preserved through Vercel rewrites. Typed FE methods support these operations;
screen controls/panels remain Task 29.

TDD: initial missing coordinator reproduced; final focused unit/HTTP/retention/real-PG suite 36
passed. Tests cover scope, one-active/global queue caps, duplicate/numeric keys,
deadlines, restart and stale completion, source eligibility, cancel/session stop,
and atomic exactly-once claim execution across two workers. Full backend 413
passed / 1 existing skip, FE/BFF 37 passed; TypeScript/focused ESLint/Web export pass.
Evidence: BE `.codex-tmp/stage6-run-full-tests.log` and
`stage6-run-final-focused-tests.log`; FE `.codex-tmp/stage6-run-portal-tests.log`
and `stage6-run-web-build.log`. Next: Task 20 configuration checker/readiness;
Tasks 20–32 and browser/header/Portal/final security-E2E acceptance remain open.

Final review caught CHECK_PENDING results surviving session stop/expiry. Failing
PG/session/retention tests reproduced it; atomic stop now settles its pending
checks, and bounded retention repairs remaining terminal pending rows. Added
worker eligibility guards for revoked source, stale config, suspension and expiry.
Updated focused run/session/HTTP/retention/PG suite: 35 passed. Final full-suite
results are recorded in the latest handoff; no new schema or migration.

Full-suite load exposed the HTTP Sandbox fixture sharing one SQLite connection
between worker/request scopes. A deterministic independent-transaction test failed
with nested-transaction errors; only the Sandbox fixture now uses an isolated named
memory DB with separate owned connections per scope. Worker remains enabled.
Focused unit/HTTP/PG/session/retention suite: 36 passed. Final full TRX is
`.codex-tmp/test-results/stage6-run-full.trx`.

## Task 20 execution evidence

Implemented the configuration checker, pre-launch owned/Admin readiness endpoint,
JSON mode on the existing pinned public fetcher, BFF GET allowlist and typed FE
client. Same checks drive persisted runs. Session/readiness fingerprints share the
existing algorithm; live actor/snapshot/proof and SSO eligibility are rechecked
after network I/O. Runtime/bridge/protocol cannot be Passed by configuration.
Disabled persisted SSO alone makes its checks inapplicable; review remains blocked.

RED: missing checker/JSON interface; missing readiness BFF route. GREEN: all 18 keys,
launch/configuration gates, no-SSO applicability, fixed catalog output, actual
OpenIddict discovery/JWKS and registry projection/credential failures, pinned TLS
JSON, owner/Admin HTTP and stale run-write coverage. Full backend 444 passed / 1
existing MoneyAudit skip; FE/BFF 39 passed; solution build, types, focused lint and
Web export pass. Details/contracts/evidence: latest handoff and operations.
TRX `.codex-tmp/test-results/stage6-config-full.trx`; FE log
`.codex-tmp/stage6-config-portal-tests.log`. Next Task 21. No model/migration,
configured DB access, deploy, commit, publication or Stage 7.

## Tasks 21–24 checkpoint — 2026-10-20

Implemented validated/confirmed host external links, opt-in SDK popup transport,
Public/Confidential completion examples and server-bound consent environment.
Backend DNS/session checks reject stale scope, other origins, auth/callback paths
and credential-bearing URLs. Bridge permission/schema/cancellation preserve v1.
Real browser Public SDK exchange/UserInfo and Confidential embedded-session
revalidation succeeded; restricted SameSite cookie variant reports unavailable
instead of embedded success. Modal confirmation/cancel/focus and noopener/
noreferrer were observed. See latest [handoff](../handoffs/developer-platform-stage-6.md)
for contracts, evidence and narrower browser limits.

RED: missing external host capability, reserved sign-in path bypass, missing popup
module and consent environment HTML/JSON. GREEN: full backend 459 pass / 1 existing skip; SDK 36
pass/build; host 18 pass; FE/BFF 39 pass; TypeScript/focused SDK lint pass; existing
HTTPS protocol E2E 1 pass. PostgreSQL uses isolated disposable storage only.

Tasks 25–32 remain pending; Stage 6 is incomplete. Final authenticated Portal,
deployed CSP/header acceptance, new-adapter COOP/storage-policy matrix, trusted
executor/readiness, all 20 E2E steps and 25-threat/18-category report remain open.
No application DB access/migration, deploy, commit, publication or Stage 7.
