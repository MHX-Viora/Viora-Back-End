# Stage 8 Web performance observations

2026-10-23. Actual Expo export and reviewed Publication, loopback HTTPS, disposable
PostgreSQL, local Chrome; one run per client type. Fixture-only bounded middleware
and DbCommandInterceptor record category/status/SQL reader count/duration, never
SQL, parameters, credentials, identity or payload. No production instrumentation.

| Observation | Public | Confidential |
| --- | --- | --- |
| Catalog count/page SQL reads | 2 | 2 |
| Catalog server time | 235.130 ms | 117.632 ms |
| Detail SQL reads per successful request | 10 | 11 |
| Launch SQL reads per successful request | 31 | 35 |
| Successful launch server durations, three samples | 792.234 / 992.319 / 559.098 ms | 587.546 / 731.240 / 634.107 ms |
| Initial browser launch-to-ready | 2284.820 ms | 2068.876 ms |
| Partner logo requests during observed host sequence | 5 | 5 |

Counts are SQL reader executions, not every possible command. Detail/launch counts
include repeated live authority checks and transaction work; two reads is only the
catalog contract. Unauthorized and preflight requests are separate samples. Logo
count is whole observed sequence, not a deduplication target or per-open quota.
No latency budget was supplied; these are local observations, not production SLOs.

CDP counters on the parent target (initial / after close-retry / after replace):

| Counter | Public | Confidential |
| --- | --- | --- |
| JS heap bytes | 11746084 / 10844064 / 11459492 | 11793652 / 10856064 / 11460276 |
| Documents | 14 / 11 / 17 | 14 / 11 / 17 |
| Nodes | 201 / 204 / 282 | 201 / 204 / 282 |
| JS listeners | 361 / 379 / 416 | 361 / 379 / 416 |

Each sample asserts exactly one top-level host iframe. Replaced sessions and
page-close server cleanup are asserted separately in `web-host.e2e.mjs`. Counts
include browser/React documents awaiting collection; only three samples, no forced
GC and no per-OOPIF heap profile. Listener/node growth remains an observation;
**no memory-leak pass or bounded long-duration memory claim** follows from it.
No native, remote-network, multi-page load or sustained stress benchmark executed.

Reproduce with operations runbook commands and `--runtime-security`; JSON evidence:
`.codex-tmp/stage8-performance-{public,confidential}.json`. See
[acceptance limits](../handoffs/developer-platform-stage-8-acceptance.md).
