# Implementation Plan: Article recommendations

## Architecture Decisions

- Reuse `PostFeedRepository` for server-side ranking and pagination.
- Keep the dedicated recommendation endpoint and interaction upsert table already introduced.
- Preserve the existing Article UI and store the selected content category in route params so refresh has deterministic state.

## Task List

### Phase 1: Audit existing continuation state

- [x] Inspect both Git worktrees, unstaged/staged diffs, recent history, migration, Article backend, client feed, and tracking code.
- [x] Run focused backend Article tests/build and client tests/type-check/lint.

### Phase 2: Close remaining behavior gaps

- [x] Add a failing client regression test proving Article-category navigation is reflected in route params.
- [x] Update Article/Community category actions with the smallest route-state change; keep Recommended as the default sort.
- [x] Add focused coverage for cold-start score bounds and recommendation handler pagination normalization if missing.

### Phase 3: Verify and review

- [x] Run backend Article tests and full single-threaded build.
- [x] Run client tests, TypeScript, lint, and web export/build if supported.
- [x] Verify EF migration/model consistency and review the final diff for correctness, security, and query bounds.
- [x] Write a concise handoff with files, migration, endpoints, ranking, and PASS/FAIL results.

## Risks and Mitigations

- Offset pagination can shift while live engagement counters change: deterministic tie-breaking plus client ID deduplication prevents visible duplicates in the current contract.
- Raw interaction upsert is PostgreSQL-specific: retain it because the existing EF provider and migration are PostgreSQL.
- The full solution test command can over-spawn MSBuild nodes in this environment: use the focused test project and single-threaded, build-server-disabled commands.
