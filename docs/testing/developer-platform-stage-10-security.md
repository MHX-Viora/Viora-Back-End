# Stage 10 — threat model and security disposition

2026-10-23. Local evidence only; no penetration test of a deployed service.
Fresh backend, browser and JS results are indexed in the handoff. Historical
Stage 7–9 security maps remain supporting context, not replacement execution.

| Surface / threats | Controls and executable evidence | Residual boundary |
| --- | --- | --- |
| Developer identity: guessing, reset/replay, session theft | `DeveloperApiTests`, identity/session tests; real registration/local SMTP/BFF login | Deployed SMTP, cookie and secret management unverified |
| BFF/browser: CSRF, token/secret exposure, proxy escape | exact origin/header/JSON allowlist, cookie policy; 63 Developer/BFF tests | Same-origin Social/Developer isolation is not an independent browser origin |
| Admin: forged role, stale/revoked source, IDOR | review auth/assignment/decision tests; actual Admin password login, stale modal prevention | Production Admin access policy unverified |
| Registry/credentials: foreign-owner mutation, rotation, environment confusion | MiniApp API/service/SSO suites; live registry projection policy | Actual encryption key custody/rotation/recovery unverified |
| Domain/network: SSRF, private/IPv6/DNS rebinding, redirects | `PublicWebAddressTests`, `PublicWebFetcherTests`, Sandbox network and domain tests; owned HTTPS proof | Public Internet DNS/hosting not certified |
| OAuth/OIDC: PKCE downgrade, code replay, state/nonce mismatch, signature/issuer/audience | `SsoAuthorizationTests`, `SsoTokenEndpointTests`, `SsoSecurityRegressionTests`; real Public/Confidential E2E | Docs standalone Public/.NET sample issuer E2E remains separate |
| Consent/linking: scope escalation, wrong subject, revoked consent | consent/account-link suites; real deny/retry/new-scope/reopen browser flow | Subject provenance stays server-derived; native handoff unresolved |
| Sandbox/evidence: forged job/proof, stale config, executor replay | executor/protocol/evidence PG tests; real 18-check browser/executor runs | Owned fixture transport does not prove deployed executor isolation |
| Review/publication: bypass, mutable snapshots, stale race, restore-republish | review/publishing/moderation PG suites and real decision flows | Deployed release approval not demonstrated |
| Host/Bridge: forged origin/channel/context, URL schemes, media, launch replay | host boundary/PG/grant tests; runtime-security E2E; 40 partner/host/cleanup tests | Chrome Web only; Android/iOS gates stay closed |
| Operations/supply chain: secrets/logs, vulnerable tools, automatic release | metadata inventory, fresh npm/NuGet scans, source/workflow review | No history-wide secret audit, external alert delivery or repository settings proof |

Unsupported refresh-token grants and refresh-token-family rotation for third-party
SSO are **N/A**: issuer enables authorization-code flow only. Social refresh-token
lifecycle tests are separate; do not report them as third-party refresh support.

## Findings / release actions

| ID / severity | Evidence / precondition | Impact, mitigation and closure |
| --- | --- | --- |
| NATIVE-01 / Critical release gate | Existing Stage 8 native SSO decision; no approved browser/WebView handoff | Keep native runtime off. Require concrete reviewed trust contract and real device/TLS/permission/SSO proof. Windows types do not close this gate |
| OPS-01 / High readiness gap | User confirms no staging/monitoring; deployed headers, issuer keys, backups and gates unavailable | Production behavior/recovery unknown. Provision owned staging, operational owners and monitored restore/alert drill before release |
| CI-01 / High release-control gap | FE/BE main push and dispatch workflows; no YAML environment gate observed | Preserve no-push/no-dispatch boundary. Operator must demonstrate protected release environment/branch controls; external settings were not inspected |
| HOSTING-01 / High readiness gap | FE deploy script runs `expo export`, without docs composition; trusted proxy setup not verified | Compose docs/host policies and demonstrate BFF/direct routes/HTTPS issuer on staging. Do not infer deployed failure from source alone |
| DEP-01 / High advisory set | Fresh npm/NuGet scans below | Classify actual runtime reachability and apply compatible fixes; unresolved applicable High findings block release. No force upgrades |
| TEST-01 / Medium reproducibility defect, fixed | Default test-class concurrency caused three `initdb` 45-second timeouts | `TestParallelism.cs` limits class concurrency to two; in-test races remain concurrent. Full suite verifies the change |
| OBS-01 / Medium readiness defect, fixed locally | `/health` returned healthy without DB probe | Added `/health/ready`, 3-second cancellation, 200/503 and no-store/minimal payload. Two actual HTTP/PG/unavailable tests pass; production probe wiring unverified |
| BUILD-01 / High packaging risk, source fix | Populated ignored local appsettings; Dockerfile uses `COPY . .` but Docker ignore did not exclude this file | Added explicit local appsettings/service-account/private-key/certificate exclusions. Git ignore alone does not protect build context. Docker is unavailable here, so inspect a clean built image before release; no historical image leak is asserted |

## Secrets/configuration scope

`tools/Stage10Audit/inventory.mjs` records paths/key names/booleans only. Ignored
local `viora-BE/appsettings.json` has populated connection/Cloudinary credentials;
this is configuration presence, not proof of exposure or valid production access.
The file is ignored by Git. No values were emitted or used to connect. No tracked
configuration candidate was found in this bounded scan. Local `.env` presence is
recorded, not its values. Git history, artifacts outside the owned exports, live
credentials and external secret stores remain unverified.

If an operator confirms exposure: restrict access, record affected key identifiers
and time window without values, rotate through the secret owner, revoke derived
sessions if needed, rebuild artifacts, inspect access logs and verify recovery.
No live keys were rotated during this audit.

## Dependency triage

Fresh scan files: FE/Admin `.codex-tmp/stage10/dependency-audit.json`, SDK
`sdk-dependency-audit.json`, BE `nuget-audit.json`. Initial counts: FE 39 (33 High,
6 Moderate); Admin 2 High; SDK 0; solution NuGet reports High
`SQLitePCLRaw.lib.e_sqlite3 2.1.6` in the test project. Counts include transitive
advisory propagation, not distinct exploitable product defects.

- `follow-redirects`: Axios Node redirect adapter. Browser Axios/BFF fetch do not
  establish that this Node path is reachable in production; compatible 1.16.1
  patch identified. [Upstream advisory](https://github.com/advisories/GHSA-q3jh-wrc6-jgq8).
- Vite/launch-editor: development server/editor endpoints; static deployed Admin
  output does not run this server. Keep dev servers private; compatible Vite
  update identified. [Advisory](https://github.com/advisories/GHSA-587h-7wmj-h5pm).
- braces/image-size/node-forge/PostCSS: primarily Expo/Metro asset/CSS/glob/dev
  tooling. Malicious build input remains a supply-chain concern. Reachability
  and patched compatible ranges require tracked dispositions; no blanket waiver.
- gRPC: Firebase transitive Node transport; no direct production import found in
  the inspected app/feature/server/service sources. Browser/native export analysis
  is required before excluding its reported TLS auth-context issue.
- decode-uri-component/uuid: transitive decoding/buffer API advisories; no direct
  vulnerable API call found in the inspected source. Deep-link dependency
  reachability still needs verification; absence of direct imports is not proof.
- SQLite native package is in the solution's test dependency graph, while the API
  uses PostgreSQL. Tool hosts have separate package graphs. Never promote these
  fixtures to production. [Advisory](https://github.com/advisories/GHSA-2m69-gcr7-jv3q).

Applied compatible patches: FE follow-redirects 1.16.1 leaves **38 advisories**
(32 High/6 Moderate); Admin Vite 8.3.5/follow-redirects 1.16.1 leaves **zero**;
separate SDK remains zero. No automatic advisory
count acceptance, major Expo migration or `npm audit fix --force` was used.
