# Implementation Plan: ANKT Sticker System

## Architecture Decisions

- Extend existing chat/SignalR payloads; keep one message flow.
- Use `Price == 0` for free packs.
- Add normalized sticker/ownership/purchase tables and `Messages.StickerId`.
- Reuse Cloudinary through a sticker upload method; no new storage provider.
- Gate paid debit behind the real wallet boundary; do not invent balances.

## Tasks

### Phase 1: Backend foundation

- [ ] Add failing domain/validator contract tests for sticker messages and pack rules.
- [ ] Add entities, EF configuration, indexes, restrictive foreign keys, and migration.
- [ ] Add bounded public catalog/detail queries with `isOwned` and `canUse`.
- [ ] Extend existing message save/history/realtime DTOs with sticker render data and server authorization.
- Checkpoint: targeted tests and backend build pass.

### Phase 2: Client vertical slice

- [ ] Add typed sticker API and message mapping.
- [ ] Add cross-platform recent-sticker persistence (max 24, MRU, no duplicates).
- [ ] Add responsive sticker panel/store and immediate-send integration.
- [ ] Add transparent `StickerMessage` rendering for history/realtime.
- Checkpoint: client tests, lint, and export/type validation pass.

### Phase 3: Admin vertical slice

- [ ] Add admin API contracts/services and authorized backend endpoints.
- [ ] Reuse Cloudinary for validated PNG/WebP thumbnail/sticker uploads.
- [ ] Add pack list/editor/detail UI within existing admin design/navigation.
- [ ] Create packs from multipart metadata plus a locally selected thumbnail; validate file size, MIME, and magic bytes before Cloudinary upload.
- [ ] Remove pack-level sort order from admin contracts/UI and drop `StickerPacks.SortOrder` through a forward migration; retain sticker-level ordering.
- Checkpoint: admin tests/build/lint pass.

### Phase 4: Paid purchase

- [ ] Bind to authoritative wallet ledger; debit using database price in one transaction.
- [ ] Record wallet transaction, purchase, and unique ownership atomically.
- [ ] Prove insufficient-balance, rollback, and concurrent double-click behavior.
- Dependency: wallet/coin boundary must be identified by the owner.

### Phase 5: Review and handoff

- [ ] Generate migration without modifying historical migrations.
- [ ] Run full builds/tests and five-axis review.
- [ ] Write concise handoff with files and PASS/FAIL matrix.

## Risks

- Dirty client chat files: preserve existing edits and make localized additions only.
- No wallet code: paid debit is blocked by an external architectural dependency.
- No existing EF integration-test project: prioritize deterministic validator/domain tests and build validation without adding unnecessary packages.
- Cloudinary-dependent manual testing requires configured credentials and a runnable database.
