# Stage 10 — bounded local baseline

2026-10-23. Measured on this shared Windows workstation while backend tests also
ran; not a staging/VPS capacity estimate. Approved publication, actual .NET issuer,
PostgreSQL and HTTPS partner/browser fixtures. No production traffic.

`load.e2e.mjs` rejects arbitrary origins/non-owned fixture mode/invalid app IDs.
It permits only fixed issuer-to-loopback transport, has a 20-second hard request
budget, 3-second per-request abort, no redirects, and two requests maximum in
flight. Each shape executes 3 ramp-up / 6 steady / 3 ramp-down requests. The
enclosing fixture owns browser/process/DB cleanup and has a 10-minute bound.
The safety test proves production/external targets cannot be selected.

| Workload | Samples / status | p50 ms | p95 / p99 ms |
| --- | --- | --- | --- |
| Published catalog | 12 / 200 | 161.24 | 324.96 |
| Published detail | 12 / 200 | 449.30 | 739.07 |
| OIDC discovery | 12 / 200 | 0.90 | 1.25 |
| JWKS | 12 / 200 | 0.91 | 1.82 |
| Invalid-client authorize denial | 12 / 400 expected | 26.02 | 141.36 |
| Invalid-grant token denial | 12 / 400 expected | 1.28 | 2.93 |

72 requests, **6,227.68 ms** elapsed; zero transport/unexpected-status failures.
Host CPU increased **453.125 ms**; working set **496,103,424 -> 495,673,344 bytes**.
12 samples make p95/p99 equal the maximum; these are noisy observations, not a
statistically adequate tail-latency or memory-leak conclusion. No 429 was observed
in this small run; rate-limit behavior is separately covered by endpoint suites.

Real production-host E2E independently records launch and three browser lifecycle
heap/DOM samples, plus two SQL reads for each successful catalog count/page.
Observed initial Web launch: Public **2,487.34 ms**, Confidential **2,867.30 ms**.
Public sample may be refreshed by later reviewed runs; logs/revision matter.
The baseline's denial workloads do not measure successful OAuth exchange load.
SQL metrics count commands/end-to-end request duration, not isolated query timing.

Fresh docs client: **342,697 bytes** raw / **94,263 bytes** gzip. Admin build warns
about a chunk above 500 kB; no measured user-facing bottleneck justifies a redesign.
Actual docs Chrome responsive/AX/search/account checks passed. Core Web Vitals,
mobile networks, browser matrix, successful auth throughput and sustained ramp
capacity remain unmeasured.

No numeric production SLO is accepted. Proposed next step: establish an idle
owned staging baseline and representative data, measure sustained success-path
catalog/launch/authorize/token plus rate-limit pressure, record resources and
errors, then review p95/error-budget/recovery thresholds with the operator.
Evidence: `.codex-tmp/stage10/baseline.json`, retained `stage10/host-performance-*.json`
and the indexed E2E logs. No optimization was made from unproven bottlenecks.
