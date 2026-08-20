# Implementation Plan: ANKT Mini App Platform v1

## Architecture Decisions

- Contract-first, single `AppDbContext`, existing `Account`/`User` and JWT claims.
- PostgreSQL launch-code rows with a concurrency token provide durable atomic consumption.
- ASP.NET password hashing protects client secrets; SHA-256 stores lookup-safe launch-code hashes; HMAC creates pairwise subjects.
- Mobile receives allowlisted domains only in the authenticated launch response and never receives client credentials.

## Task List

### Foundation

- [ ] Add entities, relationships, indexes, seed permissions, and migration. Verify: backend builds and migration script generates.
- [ ] Add contracts, error codes, URL/security policies, and failing/passing unit tests. Verify: focused tests pass.

### Public launch and SSO

- [ ] Add list/detail, consent/revoke, and launch services/controllers. Verify: auth/status/consent/domain tests pass.
- [ ] Add credential generation/rotation and atomic exchange with scoped profile projection. Verify: secret/code/subject tests pass.
- [ ] Add strict rate limits and safe audit/launch logs. Verify: Swagger and backend tests/build.

### Management

- [ ] Add admin dashboard/list/detail/lifecycle/developer APIs. Verify: role and transition behavior.
- [ ] Add developer ownership CRUD/review/rotate and partner configuration endpoint. Verify: cross-owner access is denied.
- [ ] Add admin Mini App/Developer pages and confirmation actions. Verify: admin build/lint.

### Mobile and SDK

- [ ] Remove legacy AntiFake implementation and replace utility entry with dynamic Mini App Center. Verify: no hard-coded AntiFake references in runtime source.
- [ ] Add consent flow and secured generic WebView with navigation controls, offline/error/loading/Android back behavior. Verify: mobile tests/lint.
- [ ] Add registry-based bridge and distributable JS SDK. Verify: protocol/navigation unit tests.

### Documentation and completion

- [ ] Add partner guide, Swagger tags/examples, seed guidance, and handoff. Verify: all builds/tests/lints pass.

## Risks and Mitigations

- Concurrent exchange: use an EF concurrency token and one transaction; return one generic invalid-code response externally.
- Wildcard host bypass: normalize IDN hosts and allow only exact or label-boundary suffix matches.
- Feature breadth: land vertical slices and keep unfinished management surfaces inaccessible until their APIs exist.
- Existing dirty work: preserve unrelated user changes and delete only AntiFake-specific runtime/doc assets.
