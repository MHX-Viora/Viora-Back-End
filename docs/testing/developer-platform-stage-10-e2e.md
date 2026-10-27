# Stage 10 — executable assertion map

2026-10-23. This maps inspected assertions to current runs, rather than inferring
all requirements from exit code. This map uses the approved Stage 10 spec/plan.
Exact 15/17/12/15/17 step-count certification remains open
where assertions do not directly prove a requested environment/step.

| Flow | Inspected executable assertions | Fresh evidence / remaining gap |
| --- | --- | --- |
| A: no SSO | `non-sso.e2e.mjs`: no Production client; anonymous and signed Social Utilities launch, actual website/host, Wallet read/return, close/reopen; shared `approved.e2e.mjs` obtains review/publication through real BFF/Admin | `e2e-non-sso.log` exit 0. Registration/local SMTP is separately proved by `docs-e2e.log`, not within this runner's fixture-seeded account. Deployed site/domain still unverified |
| B: Public | `web-sso.e2e.mjs`, `web-host.e2e.mjs`: real S256 flow/issuer/UserInfo, consent deny/retry/new scope, host reload/fresh reopen and one-use bound grants | `e2e-public.log` exit 0. No injected Social/Developer auth cookie. Standalone docs Public sample is a different implementation and remains Not Run against issuer |
| C: Confidential | same actual host/issuer flow plus `production-partner.fixture.mjs`: server exchange, partner session, no backend secret in browser context | `e2e-confidential.log` exit 0. New docs .NET middleware sample builds but its own HTTPS browser/backchannel E2E remains Not Run |
| D: review/publishing | `decisions.e2e.mjs`, review `moderation.e2e.mjs`, `isolation.e2e.mjs`: escaped request-changes reason, new immutable draft/checks/resubmit/approval, rejection cannot publish, old live snapshot preserved, suspended old token/UserInfo/introspection/held code denied, restore unpublished then explicit republish, expired domain/stale client denial | Public `e2e-review-baseline.log` exit 0; Confidential fresh Release all-flows disposition in handoff. Stale Debug-run failure is excluded from acceptance, retained for provenance |
| E: hostile/runtime cases | `security.e2e.mjs`, production `moderation.e2e.mjs`, HTTP/PG suites: invalid bearer cannot fall back to anonymous; cross-host/origin/channel/context and dangerous scheme denial, media policy, active suspension/code/token/launch revocation, owner and environment separation, grant replay/races | Public/Confidential runtime-security runs exit 0, 40 host/partner/cleanup tests and full backend suites. Device-specific hostile cases and full deployed browser matrix remain unverified |

Shared review runner uses real local password checks, BFF, issuer, HTTPS partner,
actual executor/browser observation, Admin decision and PostgreSQL migrations.
Its pre-seeded fixture identities/domain scenario setup are not equivalent to
end-to-end real registration/public Internet deployment in every A–E run.
Separate docs account E2E proves actual registration/local SMTP verification/login.

Known auxiliary errors: the non-SSO Social shell requests unrelated advertisement/
group-chat controllers whose DI graph is intentionally absent from the owned
fixture; request errors are logged. Required Wallet/Utilities assertions pass.
Public runtime runner can log a canceled partner request during teardown. These
are disclosed fixture limits, not a clean full-Social-app or production pass.
Do not register real external services or hide unexpected assertion failures to
remove this noise.

Use the Release production runner for current all-flow checks. The review runner
without `--production-e2e` selects Debug; its artifact must be freshly built before
use. One cached Debug Confidential attempt failed readiness and is not accepted.
The final indexed command uses the freshly built Release host instead.
