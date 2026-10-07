# Implementation Plan: Ví ANKT withdrawals

## Architecture decisions

- Extend the existing wallet aggregate and ledger.
- Model bank accounts and withdrawals as owned resources.
- Encrypt account numbers through a wallet security service; persist last-four and a keyed hash for masking/deduplication.
- Reserve funds at request creation; capture or release them during lifecycle transitions.
- Add theme-level wallet tokens instead of hard-coded component colors.

## Tasks

### Phase 1: Persistence foundation

- [ ] Add coin balance, bank-account, withdrawal entities/enums, EF configurations, and migration.
  - Acceptance: constraints, ownership FKs, unique idempotency, default-account uniqueness, and indexes exist.
  - Verify: EF migration builds and model snapshot matches.
  - Files: domain entities/enums, DbContext, wallet configuration, migration.

### Phase 2: Secure backend contracts

- [ ] Add typed bank-account/withdrawal contracts and financial/lifecycle rule tests.
  - Acceptance: inputs exclude user, wallet, fee, and balance fields; statuses and masking are explicit.
  - Verify: focused wallet tests fail before implementation and pass after.
- [ ] Implement account-number protection and owned bank-account service.
  - Acceptance: ciphertext round-trips, API only returns last-four, duplicate account detection works.
  - Verify: focused security/rule tests.
- [ ] Implement withdrawal create/get/cancel with row lock, ledger, and idempotency.
  - Acceptance: insufficient funds fail; repeated key replays; concurrent requests cannot overdraw.
  - Verify: wallet service tests and backend build.
- [ ] Implement admin list/status transitions with capture/release ledger entries.
  - Acceptance: only valid transitions occur; terminal requests cannot mutate; failure/rejection releases held funds.
  - Verify: lifecycle tests and controller contract tests.

### Checkpoint: backend

- [ ] Backend tests/build pass and migration script generates.

### Phase 3: Frontend contract and UI

- [ ] Extend wallet types/service and add reusable `AnktCoinIcon`.
  - Acceptance: typed bank-account/withdrawal API; no plaintext account field in outputs.
  - Verify: frontend contract tests and type-check.
- [ ] Upgrade wallet summary card using wallet theme tokens.
  - Acceptance: separate money/coin values and three balanced actions at 320px.
  - Verify: component contract tests, lint, responsive browser check.
- [ ] Add withdrawal form and confirmation bottom sheet.
  - Acceptance: amount presets, bank selection/addition, server fee preview, disabled/error/loading states, double-submit guard.
  - Verify: UI contract tests and type-check.
- [ ] Extend history, transaction row, and detail UI.
  - Acceptance: requested filters/status labels, signs plus icons/text, masked bank metadata.
  - Verify: formatter tests and UI contract tests.

### Checkpoint: complete

- [ ] All frontend/backend tests, builds, lint, and migration checks pass.
- [ ] Handoff documents API, migration, configuration, flow, and external-payout boundary.

## Risks and mitigations

| Risk | Impact | Mitigation |
|---|---|---|
| Concurrent overdraft | High | Serializable transaction plus `FOR UPDATE` wallet lock |
| Duplicate submission | High | Unique user/idempotency key plus client submit guard |
| Sensitive account exposure | High | Encrypt at rest, last-four API only, no sensitive logging |
| False payout success | High | Manual admin lifecycle; no automatic completion |
| Deposit regression | High | Additive contracts and full existing wallet test run |
| Dirty frontend worktree | Medium | Touch only wallet/theme files and never stage unrelated changes |
