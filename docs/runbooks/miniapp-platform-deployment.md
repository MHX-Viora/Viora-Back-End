# Mini App platform — deployment, recovery and incident runbook

2026-10-23. **Reviewable procedure; not executed production instructions.**
Current release verdict is [NO-GO](../releases/miniapp-platform-production-readiness.md).
There is no staging/monitoring environment available. Do not run production
steps until the release owner closes the checklist and explicitly authorizes them.

Repository observations: FE `/var/www/viora/frontend`, BE
`/var/www/viora/backend`, compose root `/var/www/viora`, service `viora-api`,
frontend `https://mxh.ankt.vn`. These have not been verified on a VPS. The operator
must supply confirmed `API_ORIGIN`, issuer, host/Sandbox origins, `PORTAL_ORIGIN`,
compose file, image/artifact IDs, DB backup/restore targets, secret store/key
identifiers, Nginx config and monitoring destination. Do not guess missing values.

## 1. Authorize scope and assign owners

Record approved Web/native scope, release/security/DBA/SRE owners, maintenance
window, entry/stop criteria and rollback decision maker. Stage 10 local approval
does not grant production access. Keep native disabled until its P0 is closed.
Do not push/dispatch the existing automatic main deployment to request review.

## 2. Inventory current and previous release

Capture exact FE/BE/Admin revisions, dependency locks, public build configuration,
artifact hashes and migration level. Retain immutable previous images/static
artifacts and the config/key version map. The local dirty source digest in
`stage10/inventory.json` is not a clean release artifact identifier. Verify real
paths and compose service names before any restart/replacement.

## 3. Verify configuration and trust

Confirm trusted reverse-proxy forwarding, HTTPS/HSTS, exact issuer/discovery/JWKS,
dedicated host/Sandbox/portal origins, exact callbacks, CORS/CSP/permissions/cookies,
request sizes/rates, DB role/pool limits and durable encryption/DataProtection
keys. Main startup has no explicit `UseForwardedHeaders`; prove actual proxy
handling before OAuth testing. Never disable TLS validation or print secret values.
Inspect the built image/context for ignored local appsettings/private key files;
the Docker ignore source fix is not evidence that old images never held them.

## 4. Back up and rehearse recovery

Create an access-controlled database backup and separately protected config/key
backup, retaining identifiers/checksums rather than values in the release record.
Restore into an owned isolated target and compare schema, migration history,
representative data and integrity. Measure production-volume recovery against
operator-approved RPO/RTO. The 309/910 ms synthetic local backup/restore sample
is not an operational target or key-recovery test.

## 5. Review migration compatibility

Generate/review the exact SQL between the deployed and proposed schema levels.
Test representative old data, indexes/triggers/locks, resource impact and previous
application compatibility on the upgraded schema. Use a controlled migration
job with the confirmed target and operator approval; never default to appsettings
or a fixture command for a live database. Do not assume `Down` is safe.

## 6. Prepare and verify artifacts without deployment

Run the [spec commands](../specs/developer-platform-stage-10.md) with the current
locks and clean revision. Build API, Admin, Expo and docs; compose docs into the
Expo artifact using `scripts/build-developer-docs.cjs --base-export ...` with
reviewed owned workspace paths. Compose Sandbox/Production security policy as
documented in Stage 8/9 operations. The current FE deployment script only exports
Expo and does not perform this docs composition. Proposed CI gates: build/types/
lint, backend/PG suites, BFF/SDK/docs tests, migration/recovery check, current
advisory triage, real browser fixtures and artifact/image secret inspection.
Keep verification jobs secret-free and separate from deployment approval.

Fixture artifacts under `.codex-tmp/stage7-*`, `stage8-*`, `stage9-*` embed owned
test API/origins and cannot be deployed. Rebuild with approved public production
configuration; do not copy `.env`, keys, logs, fixtures or source maps into hosting.

## 7. Prove the candidate on owned staging

Deploy only to a confirmed isolated staging target. Verify Nginx/BFF/static docs
route order, deep refresh/404, metadata, security headers, host iframe/callbacks,
real Developer SMTP, identity sessions, actual Public/.NET reference samples and
complete A–E assertions. Exercise DB outage/readiness and alert delivery. Test
previous-app rollback compatibility and controlled suspension before signoff.
This step is currently blocked because the user has no staging environment.

## 8. Execute an explicitly approved production change

Require an enforceable release/environment approval and confirm protected-branch
rules externally; YAML alone cannot prove them. Use pinned reviewed artifacts,
approved migration/config/key versions and the verified compose/Nginx procedure.
Record start/end times and operator actions. This audit does not change workflow
triggers, invoke SSH, migrate, restart or deploy. Never enable all runtime/native
flags merely because a local fixture passed.

## 9. Smoke-test the actual deployment

Using operator-confirmed origins, verify liveness and DB connectivity separately:

```bash
curl --fail --silent --show-error "$API_ORIGIN/health"
curl --fail --silent --show-error "$API_ORIGIN/health/ready"
curl --head --fail --silent --show-error "$PORTAL_ORIGIN/developers/docs"
```

`GET /health/ready` is additive: 200 `{"status":"ready"}` or 503
`{"status":"not_ready"}`, `Cache-Control: no-store`, DB probe cancellation after
three seconds. It proves connectivity, not schema compatibility, mail/executor
health or overall readiness. Verify direct docs/unknown-route 404, BFF cookies,
discovery/JWKS exact issuer, test-owned catalog/launch/SSO/review and Wallet return.
Do not log token/code/verifier/cookie/secret payloads.

## 10. Observe and expand deliberately

Internal users -> developer pilot -> limited cohort -> wider cohort -> GA.
Each expansion requires a stable observation window, signed security/readiness
gates, successful business probes and reviewed latency/error/resource budgets.
Stop on unauthorized access, token/secret exposure, duplicate authority/state,
schema incompatibility, broken launch/SSO or operational alerts. Numeric SLOs and
cohort sizes await actual staging resources and release-owner review. Configure
structured trace IDs, restricted log retention and auth/OAuth/Sandbox/publishing
metrics; demonstrate a real alert reaches the responsible operator.

## 11. Respond, contain and roll back

Stop expansion, preserve sanitized evidence and identify the affected
revision/environment/key IDs. Suspend affected publications/revoke authority via
reviewed operational procedures. If compromise is confirmed, have the secret
owner rotate keys and revoke derived sessions; do not paste values into incidents.
Restore the previous compatible application/static artifacts and matching config
through the approved service procedure. Leave an additive schema in place when
compatible; prefer forward-fix otherwise. Restore data only with explicit DBA/
release-owner authorization and a rehearsed recovery point. Re-run smoke/alert
checks and record incident cause, actions, recovery time and residual risk before
reopening traffic. No destructive database rollback command is presumed safe.
