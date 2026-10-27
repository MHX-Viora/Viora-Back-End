# Developer Platform Stage 8 implementation checkpoint

Native continuation (2026-10-23): Android debug build now passes and an Android 15
emulator is connected. Two native teardown defects fixed; native P0/device/iOS
acceptance remains outstanding. See [native checkpoint](developer-platform-stage-8-native.md).
Historical no-device statements below are superseded.

Latest status (2026-10-23): **isolated Web milestone complete; native not accepted**,
as chosen by the user. The final checkpoint below supersedes historical pending
Web items. See [acceptance and limits](developer-platform-stage-8-acceptance.md).

2026-10-22. User approved the existing spec/52-task plan with “chạy làm giai đoạn 8”.
Implementation in progress; no Stage 8 completion or native acceptance claimed.
Preserve existing unrelated edits (baseline: 194 backend / 176 frontend working
entries); no commit, deployment, application DB migration, email or Stage 9.

## Initial platform audit

- Windows workstation: Android SDK/platform tools/emulator/system-images and Java
  exist. `adb devices -l` returned no attached device. Emulator availability will
  be checked separately; this is not evidence of a runnable Android build.
- No iOS project/Xcode environment; iOS native E2E remains NOT VERIFIED.
- Expo 54 local compatibility manifest proposes react-native-webview 13.15.0;
  package not yet installed at this checkpoint. Dependency/advisory gate precedes
  installation; P0 native host/SSO feasibility remains open.
- Stage 4/common publication/catalog work may proceed independently, as permitted
  by the approved plan. Existing Stage 5–7 results are historical baseline, not
  executed Stage 8 tests.

## Verified backend foundation

- Task 4: public DTO/pagination and disabled-by-default bounded origin/lifetime
  policy: RED missing types, GREEN 19 tests.
- Task 5: shared immutable publication authority, non-SSO eligibility without an
  OAuth client, live domain/developer/client checks: GREEN 40 focused tests including
  unchanged SSO/publication regressions.
- Tasks 6–8: immutable catalog projection, exact same-app publication/version/review
  FK, PG snapshot-matching insert/update/delete guard, publish transaction integration
  and rollback/replay coverage. GREEN 11 transaction/migration tests including real
  disposable PostgreSQL Up/Down/Up and Stage 7 migration regressions. Initial PG
  fixture correctly rejected inserting an already-approved review; fixture now uses
  the actual review/approval/publish service workflow instead of relaxing guards.
- Task 10 is split before implementation: 10a pins non-secret approved client
  key/type/scopes in the projection so SQL filtering can compare exact live client
  state before pagination; 10b implements bounded catalog queries/endpoint tests.
  Generated migration/Designer/model changes are separate from authored slices.

## Catalog checkpoint A

- Tasks 9–11 complete locally: per-app operator dry-run/apply reconciliation,
  immutable client key/type/scopes, live SQL filters before count/page, anonymous
  bounded list/detail endpoints, no-store, module-scoped rate limit and safe 503.
- Real PostgreSQL query tests cover non-SSO proof expiry and SSO client/credential
  changes before pagination. Real approval/publish projection and migration tests
  remain separate from explicitly labelled unit/provider query data.
- Full Release backend regression: **757 passed / 1 existing MoneyAudit skip / 0
  failed** (`.codex-tmp/stage8-backend-checkpoint-a-final.log`). An older fixture
  needed its evaluation clock advanced after creating its publication; future
  publication protection remains intact. Debug build encountered a DLL held by
  another .NET process; Release isolation avoided stopping that process.
- Reconcile tool Release build: zero warnings/errors; invalid app ID and missing
  explicit operator connection reject before database access. No application DB
  reconciliation/migration executed. Service dry-run/apply/replay tests pass.
- Task 25 typed anonymous Web/native-compatible display client/cache implemented:
  four tests pass and root TypeScript check passes. Cache is bounded to five pages
  for 30 seconds and never authorizes a launch. No Social auth interceptor used.
- Task 26 split into grid/layout and state/section slices: narrow/large-font layout,
  skeleton/empty/error/retry/load-more, stale-response cancellation and deduplication.
  Layout and state tests pass; UI browser acceptance and Utilities integration are
  pending. Components are not mounted by the application yet; no dead launch action.
- Next: production host session/grant schema and access/binding/runtime, then wire
  Utilities/Web host. Stage 8 is still IMPLEMENTING, not accepted or deployed.

## Production host foundation

- Task 12: session/grant contracts and relational mappings, paired optional
  actor/source, exact publication, binding/request uniqueness, hashed credentials
  and encrypted replay receipt field: RED missing types, GREEN 7 tests.
- Task 13: additive host migration, PostgreSQL authority/retarget/reopen/replay
  guards; GREEN 7 migration tests including catalog and Stage 7 regressions.
  Offline EF model has no pending changes (existing Report warning unrelated).
- Task 14: anonymous source policy and signed bearer authentication; supplied
  invalid/expired/wrong-signature/type/session bearer never becomes anonymous.
  Exact HTTPS Web Origin and live account/social sid checks; GREEN 2 tests with
  real JWT middleware. No Social/SSO session created or auth behavior changed.
- Task 15: typed immutable runtime event, fixed codes and same-app session FK;
  no free text/URL/token/claim fields. GREEN 2 persistence tests.
- Task 16 splits into transactional writer/tests, then migration/PG guards and
  fixture dependency cleanup. Generated EF output tracked separately.
- Frontend section/grid root TypeScript check passes. Application integration
  and actual browser validation await the working launch endpoint.

- Task 16 complete locally: transactional audit writer, immutable/provenance PG
  guards, one-time action uniqueness and minimum retention guard; GREEN 7 focused
  audit/host/Stage 7 migration tests. Task 17 complete: encrypted host-only browser
  cookie and separate short-lived nonce-bound launch challenge; GREEN 3 binding
  and 2 HTTPS host endpoint tests. Disabled runtime exposes no host routes.
- Security review before launch found two gaps and closed them: SSO-required apps
  are filtered before pagination when Sso:Enabled=false (15 query/API tests); Stage
  8 eligibility compares actual live client type/scopes before the Stage 5 reader
  projects approved metadata (36 eligibility/SSO/launch regressions pass). Existing
  Stage 5 snapshot semantics remain intact.
- Task 18 split: policy/preflight/transaction core, PostgreSQL concurrency/authority
  locks, then strict HTTP command wiring. Core GREEN 2 tests; PostgreSQL GREEN 2
  tests demonstrate single-winner quota and domain revocation blocked by explicit
  row locks until launch commits. No network inside the transaction; source and
  policy revisions rechecked after preflight. Launch HTTP endpoint pending.
- Frontend catalog UI renderer tests: 3 pass (escaping/accessibility/fallback,
  skeleton and empty/error/load-more). These are component checks, not browser or
  business E2E acceptance. Native remains disabled/unverified.

## Web runtime API checkpoint (ongoing)

- Task 18 HTTP command complete: strict bounded JSON (unknown/duplicate keys,
  depth/size/content-type), exact HTTPS first-party Origin, signed bearer source
  and disabled native rejection; endpoint test passes. No URL override accepted.
- Task 19 complete locally: one-use browser/nonce/audience-bound redemption, CAS
  grant + runtime hash + transactional audit; receipt ciphertext erased on redeem.
  Runtime credential goes only into a scoped host-only Secure/HttpOnly cookie and
  is excluded from JSON. GREEN 8 grant/launch tests, 1 post-CAS rollback test,
  1 real PG concurrent redeem test, 4 host/launch HTTP tests.
- Task 20 core and HTTP implemented: v1 context, rate-bounded heartbeat without
  expiry extension, authenticated scoped/idempotent close, live policy/source
  recheck and persisted invalidation. GREEN 5 core and 8 combined HTTP/core tests.
  A manually closed hash is retained only for authenticated idempotent close;
  ClosedAt denies all context/heartbeat. Invalidation clears the hash. Retention
  must clear historical closed sessions. No Social logout or token mutation.
- PG context/close race and invalidation tests GREEN 2, with unchanged unrelated
  Social session. Initial assertion assumed a single refresh token in the entire
  fixture, which also includes an Admin session; assertion now scopes the exact
  account being protected. Combined earlier PG launch/redeem cases GREEN 3.
- Task 21 will split into fixed-code event ingress, expired-grant/session cleanup
  and quota correctness, then worker/registration/options. Important pending check:
  expired unredeemed grants must stop occupying active-binding quota after 30s,
  rather than holding a slot for the 15-minute session expiry. Enforce a bounded
  per-binding/account launch rate in addition to IP throttling. Preserve cleanup
  during rollback; no DB work or runtime UI has been enabled in app configuration.
- Tasks 22–24 were subsequently implemented; see the Web integration checkpoint
  below. Actual business E2E and native acceptance remain outstanding.

## Runtime/cleanup verified checkpoint

- Task 21 locally verified: fixed-code event ingress with 10/minute/session quota
  and no arbitrary payload; HTTP unknown/server-only codes rejected. Per-binding/
  account launch creation quota and expired-unredeemed slot release: GREEN 5
  quota/launch tests. Retention batch bound 100 proven; worker opt-in/default-off
  and cleanup-only validation verified. PostgreSQL cleanup with real guards and
  active session preservation + launch SQL regression: GREEN 3 tests.
- Full Release backend regression after Tasks 4–21: **813 passed / 1 existing
  MoneyAudit skip / 0 failed**, log `.codex-tmp/stage8-backend-runtime-checkpoint.log`.
  Root frontend types and all 10 catalog/client/cache/grid/renderer tests pass.
- Task 22 splits before implementation: actual configured issuer/Sandbox hostname
  reservations, strict private cookie parsing, host-only middleware boundary,
  then document/assets/CSP and private parent transport. Avoid exposing other API,
  Swagger, issuer or Developer paths on the production host hostname, including
  rollback while the runtime flag is off.
- CSP architecture: pre-launch shell has frame-src none and contains no app data.
  After one-use redemption it navigates to a host run document identified only by
  a non-authorizing session ID. Server selects exact partner CSP after validating
  the host runtime and binding cookies plus live policy/source. Document reads
  do not authorize bridge/actions; the in-memory parent must return its nonce via
  exact-origin/source private handshake before the frame/bridge becomes active.
  Grant/nonce/token never enters URL or app persistence. A fresh document needs a
  fresh private generation handshake; process restart creates a new launch.
- Task 23 preserves original v1 wire semantics only; Task 24 owns lifecycle and
  explicit external navigation. No unsupported v2/native/auth permission claims.

Native: no attached device or configured AVD was found. User selected continuing
backend/Web without a native device; native P0/E2E remain unverified, not cancelled
requirements. Installed WebView 13.15.0 with scripts disabled; npm audit has no
WebView advisory entry, but the existing dependency tree reports 44 advisories
(11 moderate/33 high). No unrelated dependency upgrades were made. Known upstream
WebView UXSS advisory affects <=10.10.2, outside the selected pin; native runtime
and permission-denial proof is still required before enabling it.

## Web integration checkpoint

- Tasks 22–24 implemented: hostname boundary remains isolated during rollback;
  exact reserved issuer/Sandbox hosts, strict unique private cookies, protected
  shell/run HTML, exact partner CSP and embedded assets. Read-only document cookie
  validation does not authorize actions; parent resume with an in-memory nonce is
  required before mounting partner or activating bridge. No grant/nonce in URLs.
- Production v1 bridge supports context/close only, with exact origin/source,
  schema, handshake, request quotas and abortable document generations. Background
  pauses actions; foreground checks live context. External navigation requires
  fresh server approval, expiring confirmation and a genuine trusted click.
  Host Node tests: 12 pass; document/header/navigation backend group: 5 pass.
- Tasks 25–29 implemented locally: strict catalog client/display cache/responsive
  section, Web-only Utilities insertion immediately below unchanged Wallet,
  exact anonymous route exception `/mini-apps/[appId]`, host state controller and
  Web iframe adapter. Double press/stale responses/disposal are guarded. Launch
  always POSTs fresh, bearer only goes to configured API, invalid bearer fails
  without anonymous retry. Grant is dropped after private host handoff. Native
  adapter explicitly unavailable. No persisted host credentials.
- Task 30: additive exact-route CSP generator composes existing Developer/Sandbox
  baseline without BFF rewrites. `npm run build:web:mini-apps` writes only local
  `.codex-tmp/stage8-web-export`, including generated vercel.json; no deployment.
  Export passed using explicit fixture origins and EXPO_NO_DOTENV=1. The first
  sandboxed run could not spawn Metro workers; approved local test context passed.
- Task 31: SDK v1 getEnvironment derives existing getContext; isInsideAnkt reports
  verified/non-disposed connection only, not server authorization or partner login.
  No new v1 permission/method. Full SDK suite: 37 pass, v2 Sandbox preserved.
- Frontend checkpoint: 19 TypeScript feature tests + 15 UI/Wallet tests pass;
  root tsc passed before final integration and is rerunning after route changes.
  A review found iframe load could overwrite an earlier verified handshake in the
  parent status; regression now preserves bridge.connected until disconnection.
- Chrome static-export preview: exact public route stays anonymous; API connection
  refusal produces safe retry UI, zero iframes, 44px controls and keyboard focus on
  close. This is UI-only evidence, not published-app/bridge/SSO business proof.
  Initial viewport resize was affected by existing browser zoom; do not report
  requested widths as measured widths until rechecked with zoom reset.
- Full backend regression is rerunning after host changes. Initial sandboxed run
  could not start/stop isolated PostgreSQL (76 infrastructure failures); approved
  fixture-only run is pending. Never interpret that run as application failures
  or connect to the application database to bypass fixture restrictions.

Remaining: complete real Web responsive/lifecycle and published business E2E,
SSO Public/Confidential, hostile/race matrix, configurable module quotas review,
final checks/security review and acceptance mapping. Stage 8 remains in progress.

## 2026-10-23 continuation — Web UI, quotas and real host pipeline

- The previous pending checks completed: `stage8-host-backend-regression.log`
  records 823 passed / 1 pre-existing MoneyAudit skip / 0 failed; frontend
  `stage8-integrated-types.log` is clean. These are prior-run evidence, not this
  continuation's final regression.
- Task 32 UI-only harness now reproducible: actual React Native Web section/grid/
  screen/controllers with simulated services. Chrome exact 320/375/768/1440,
  44px controls, zero horizontal overflow, escaping, pagination/error/retry/empty/
  reconnect, keyboard and host lifecycle checks pass; zero console issues or
  external requests. Fixed lost focus when entering the host; the real screen
  now focuses its close control. Screenshots inspected at 320 and 1440.
  A fixture Wallet sentinel is explicitly not actual Utilities/Wallet acceptance.
- Task 21 quota follow-up: validated configurable HTTP prepare/launch/redeem/
  runtime and per-session event rates; binding/account history uses the configured
  launch rate. Defaults and hard ceilings preserved, independently configurable
  downward. RED missing options, GREEN six policy/bounds tests; RED three lowered
  core quotas not enforced, GREEN combined 13 HTTP/core quota tests. No shared
  Wallet/Social/Developer limiter changes. No schema changes.
- Task 45 split into partner v1 runtime, actual Expo HTTPS portal, then real
  pipeline hook after Stage 6/review/approval/publication. `ProductionFixture.cs`
  had stale API/DbSet names; Release compilation exposed them and now passes
  with zero warnings/errors. `run-fixture.mjs` reuses the existing review fixture
  lifetime/cleanup. Every approval/publication is an actual business operation;
  no seeded successful reviews or host sessions.
- First real Web attempt caught an export bug: Metro retained old inlined API/
  host origins while generated CSP used new origins. Network refused the old
  API; UI safely showed retry with zero iframes. Build now clears Metro cache;
  rebuilt bundle checked for the expected isolated issuer/host origins.
- Actual Public pipeline passed (exit 0): Developer BFF login, Admin password/live
  session, all 18 real Stage 6 checks, submit/lost-reply exact replay, approval,
  publication/catalog/detail, actual exported anonymous route, prepare/launch/
  redeem/v1 SDK context/close, fresh retry/reopen and scoped replacement-session
  closure. Actual host has no horizontal overflow at measured 320/375/768/1440;
  parent runtime errors/warnings zero. The existing standalone Public Production
  OIDC regression also passes. This opens an **SSO-enabled** reviewed publication
  anonymously; it does not yet prove non-SSO Flow A or SSO inside the host.
- Latest frontend checks: 34 feature/Wallet tests, 63 Developer/BFF tests, root
  tsc, focused lint and isolated-origin Web export pass. Node worker/esbuild/
  Chrome needed fixture-only execution outside sandbox (`spawn EPERM` inside).

Evidence: backend `.codex-tmp/stage8-resume-production-public.log`,
`stage8-resume-web-ui.log`, `stage8-quota-{config,services}-{red,green}.log`,
`stage8-resume-fixture-build.log`; frontend `stage8-resume-{ui-tests,developer-tests,types,lint,export}.log`.
See [security map](../testing/developer-platform-stage-8-security.md) and
[frontend handoff](../../../viora/docs/handoffs/developer-platform-stage-8.md).

Confidential continuation: actual workflow, invalid-bearer rejection and page-close
server cleanup passed. Initial screenshot capture timed out; explicitly activating
the parent target before capture resolved this, and CDP errors now retain the
method/caller stack without request arguments. Screenshots at 320/1440 inspected.
The first scenario-successful runner still failed its final browser cleanup:
stale parent receipts attempted cleanup after the child had removed its profile.
Extracted owned cleanup, reproduced that case RED, then GREEN two tests proving
missing-profile idempotency and preservation of an existing unowned-PID profile.
Canonical temp bounds/symlink denial and exact Chrome/PID/profile checks remain.
The final Confidential runner exits 0, including actual host + standalone OIDC;
all fixed review/Production fixture ports are closed afterward. Logs:
`stage8-resume-production-confidential.log`, `stage8-browser-cleanup-{red,green}.log`.
Public passed before the added screenshot/invalid-bearer/page-close assertions;
those added checks are independently proven by the final Confidential run.

Final checkpoint for this continuation: backend Release regression **832 passed /
1 pre-existing MoneyAudit skip / 0 failed**; log
`.codex-tmp/stage8-resume-backend-regression.log`, TRX
`.codex-tmp/stage8-resume-results/stage8-resume-final.trx`. Release fixture build:
zero warnings/errors. Review covered correctness, source/data isolation, bounded
quotas, cleanup ownership, fixture evidence scope and compatibility. No blocking
finding remains in these completed slices; this is not final Stage 8 acceptance.
Remaining: actual Utilities/Wallet business integration, non-SSO publication Flow A,
embedded Public/Confidential Production SSO, full active-runtime suspend/restore
and hostile/race matrices, measured performance and final acceptance report.
Native remains explicitly disabled; P0, device permission proof and native SSO/
iOS execution are not verified. No deployment, application DB migration, real
email, commit or Stage 9. Preserve all pre-existing unrelated working changes.

## 2026-10-23 — Web completion; native explicitly deferred

User chose **“Chốt Web trước; ghi native chưa nghiệm thu.”** This checkpoint
supersedes the preceding Web remaining-work list. The
[acceptance report](developer-platform-stage-8-acceptance.md) maps actual A–D Web
flows, 16 backend/20 frontend local coverage groups and 21 delivery topics.
Android/iOS remain NOT VERIFIED; full cross-platform Stage 8 is not complete.

- Embedded Production SSO now runs inside the actual exported host: Public SDK
  memory-only flow and Confidential partner-owned backend exchange/session.
  Both prove deny/retry/allow, repeat fast path, added scope consent, real code/
  token/ID token/UserInfo, reload and fresh-host reopen. A popup receipt alone
  grants no authority. Four partner-server boundary tests cover unauthenticated
  completion, public metadata, Public callback and rejected input/method/origin.
- Actual non-SSO publication has no Production client. Real Social password
  login, WalletService and persisted wallet, Utilities placement, anonymous and
  authenticated host, close/return and one Wallet focus refresh pass. Expired
  domain hides catalog while Wallet remains usable. Unrelated authenticated
  ads/chat/background controllers are not fully registered in this isolated
  fixture and log missing DI services: this is not a whole-app zero-error pass.
- Actual Admin suspend/restore/republish runs for both Public and Confidential:
  catalog/detail/new launch blocked, active host invalidated, token/introspection/
  held code denied, restore remains unpublished, explicit republish opens fresh
  host; unrelated Social sessions preserved. Shared partner shell no longer
  prevalidates an old Sandbox cookie before selecting its runtime; the Sandbox
  module independently revalidates its own session. Full Stage 6 checks still pass.
- Actual hostile Web messages cannot close the live host; SDK context still
  works. Dangerous scheme denied; camera/mic/geolocation policy disabled and
  media request denied. This supplies no native permission proof.
- Five domain security cases add account/source/anonymous receipt isolation,
  revoked-source redemption rollback and cross-browser runtime/close isolation.
  A real PostgreSQL lock test proves exact source revocation serialization and
  independent unrelated source. These supplement signed-JWT endpoint tests;
  domain Actor objects are not themselves signed identity evidence.
- Redaction test exposed record-generated ToString leaking binding/nonce/grant.
  Four private request/receipt DTOs now override formatting; required JSON wire
  fields are unchanged. Regression proves both redaction and serialization.
- Bounded fixture-only performance interceptor and three lifecycle samples are
  recorded in the [performance report](../testing/developer-platform-stage-8-performance.md).
  Real catalog uses two SQL reads. No production latency or memory-leak claim;
  listener/node counts vary and no forced GC/long-duration profile was run.

Final business runners all exit 0:
`.codex-tmp/stage8-completion-security-public-final.log`,
`stage8-completion-security-confidential.log`, `stage8-completion-non-sso-final.log`.
The first two include `--runtime-security`; third uses `--non-sso-partner`.
Reproduction and rollout limits: [operations](../operations/developer-platform-stage-8.md).

Web review covered correctness, auth/source/transaction isolation, private data,
bounded stores/cleanup, SDK compatibility and honest fixture/platform limits.
No high-severity finding remains in these reviewed Web slices. Runtime defaults
off; no live application DB access/migration, real email, deployment, commit,
native enablement or Stage 9. Preserve all pre-existing unrelated edits.

### Final verification for this Web checkpoint

- Backend Release regression: **839 passed / 1 pre-existing MoneyAudit skip /
  0 failed**, exit 0. `.codex-tmp/stage8-completion-backend.log`; TRX
  `.codex-tmp/stage8-completion-results/stage8-completion-final.trx`.
- Release solution and fixture builds: zero warnings/errors. Model-only check:
  no pending changes, synthetic `VIORA_EF_DESIGN_CONNECTION` (no application
  settings/database). Existing unrelated ReportReason default/sentinel warning
  remains. Logs: `stage8-completion-{release,fixture-build,model}.log`.
- Frontend: **41** Mini App/Wallet/root SSO tests, **63** Developer/BFF tests,
  **37** SDK tests; root tsc, strict fixture tsc and focused lint pass. Static
  React Native Web rendering emits the existing `props.pointerEvents` deprecation
  warning; explicit Node exit code is zero. Frontend logs:
  `.codex-tmp/stage8-completion-{feature-tests,feature-warnings,developer-tests,sdk,types,lint}.log`;
  backend fixture type log: `stage8-completion-fixture-types.log`.
- Node partner/owned-browser/Production host suites: **18 passed**, zero failures;
  `stage8-completion-host-tests.log`. New backend security/redaction/real-source
  race cases also pass within the full regression above.
- Final Public + Confidential `--runtime-security` and non-SSO runners exit 0.
  Explicit loopback connection probes after cleanup found no listeners on
  443/5490/5493/5494/5495/5593/5594. No arbitrary process cleanup was used.

Web milestone complete within the acceptance report's local fixture limits.
Native Android/iOS, native SSO P0 and native permission proof remain unaccepted
by explicit user scope. Deployed/browser-matrix verification is a rollout gate.
