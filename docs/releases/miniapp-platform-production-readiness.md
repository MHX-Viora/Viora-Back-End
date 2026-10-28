# Mini App platform — production readiness

2026-10-23. **NO-GO.** Local audit authorization is not release approval.
The user confirms no staging, Mac/iOS environment or external monitoring is
available. Owners below are proposed roles, not confirmed assignments.
Read with the [handoff](../handoffs/developer-platform-stage-10.md),
[security findings](../testing/developer-platform-stage-10-security.md) and
[runbook](../runbooks/miniapp-platform-deployment.md).

| # | Gate / status | Current evidence | Proposed owner | Risk / required closure |
| --- | --- | --- | --- | --- |
| 1 | Architecture — assessed | 18-component authority map; no new trust boundary | Backend lead | Confirm deployed services match reviewed source/artifact revisions |
| 2 | Developer identity/BFF — Passed locally | HTTP suite, 63 BFF tests, real registration/SMTP/login browser | Identity + frontend | Verify actual mail delivery, cookies, BFF secret and session lifecycle on staging |
| 3 | Registry/credential custody — Partial | ownership/rotation/revocation tests; encrypted registry authority | Backend + security | Demonstrate actual key custody, version separation, rotation and recovery |
| 4 | Domain/SSRF — Passed locally | address/network/domain tests, owned HTTPS proof | Backend + operations | Verify public DNS/TLS/root-proof hosting and expiry/revocation operations |
| 5 | Public/Confidential OAuth — Passed locally | real issuer/partner/runtime-security E2E | Identity | Verify actual issuer/JWKS/callback/environment configuration and partner integrations |
| 6 | Consent/linking — Passed locally | HTTP and browser deny/retry/scope/revoke cases | Identity | Demonstrate deployed subject/session provenance and incident revocation |
| 7 | SDK/docs/examples — Partial | 37 SDK tests; 20-page docs/account browser; .NET sample build | Developer experience | Run standalone Public and actual .NET middleware sample issuer E2E; SDK remains private |
| 8 | Sandbox/executor — Passed locally | fresh 18-check real executor/browser runs, PG evidence tests | Backend + security | Verify separate deployed origins, executor custody, TLS and quotas |
| 9 | Versions/reviews — Passed locally, final run indexed in handoff | immutable snapshot/PG races; actual Admin decisions | Backend + Admin | Require current Public/Confidential all-flow results and deployed reviewer access policy |
| 10 | Publishing/moderation/catalog — Passed locally | approved-version publication; suspension/code/token denial; restore-unpublished | Backend | Verify operational suspension and explicit republish on staging |
| 11 | Web host/Bridge — Passed locally | runtime-security, source-bound grants, close/reopen; Chrome responsive | Frontend + security | Browser matrix, actual host origins/CSP/BFF routing and TLS still required |
| 12 | Android/iOS — Blocked | Android emulator connected; no accepted device run; iOS tools unavailable | Mobile lead | Resolve native SSO P0 and prove real permissions/lifecycle/TLS. Keep native gate off |
| 13 | Database/recovery — Partial | 71 migrations, old synthetic data, actual separate DB restore and schema/integrity comparison | DBA + operations | Production-size/role/key/offsite recovery and application rollback drill; approve RPO/RTO |
| 14 | Performance — Partial | six bounded shapes/72 requests, browser launch and resource samples | Performance + operations | Representative staging, sustained success-path load, query profiles and reviewed SLOs |
| 15 | Security/observability — Partial, release blocking | patched Admin dependencies; remaining FE/NuGet advisories; tested DB readiness; Docker ignore fix | Security + SRE | Resolve/appraise applicable High advisories; inspect built image; wire readiness/log retention and test external alert delivery |
| 16 | CI/deployment/release — Blocked | workflows reviewed, local checks executed; no production mutation | Release owner + operations | Compose docs/host policy into actual artifacts; trusted proxy/HTTPS proof; enforce approvals, confirm prior artifacts and sign off GO |

GO requires every applicable mandatory gate to have current environment-specific
evidence and explicit release approval. A Web-only release, if later requested,
must have its own reviewed scope and enforceable disabled native gates; it is
not full cross-platform acceptance. No rollout is authorized by this document.
