# Sticker System Handoff

## Status

Implemented the database-driven free/owned sticker catalog, existing-chat integration, realtime/history payloads, Cloudinary-backed admin upload, mobile/web picker/store/recent storage, and admin management. Paid debit is intentionally not implemented because no wallet, balance, coin, or wallet-transaction boundary exists in any backend source or migration; inventing one is prohibited by the feature boundary.

## Database

- New: `StickerPacks`, `Stickers`, `UserStickerPacks`, `StickerPackPurchases`.
- Changed: nullable indexed `Messages.StickerId` FK.
- Unique: `UserStickerPacks(UserId, StickerPackId)`.
- Delete behavior: all sticker/ownership/message references are restrictive; admin actions soft-disable.
- Free state: `Price == 0`; no duplicate `IsFree` storage.
- Migration: `20260908045331_AddStickerSystem` (model snapshot synchronized; not applied to a live database).
- Migration: `20260908074713_RemoveStickerPackSortOrder` drops the pack-level sort column and replaces its composite index; apply it after `AddStickerSystem`.

## Backend

- Public authenticated catalog/detail APIs under `/api/sticker-packs` with bounded pagination and `featured/free/paid/owned/usable` filters.
- Existing `POST /api/chat/messages` accepts `stickerId`; validator forbids client content/attachments for stickers.
- Existing repository verifies conversation membership, sticker/pack active and availability, and free/owned entitlement before saving.
- Message history and SignalR `ReceiveMessage` carry URL metadata resolved from the FK; recipients do not need ownership.
- Existing forward-message flow applies the same active/ownership checks and preserves `StickerId`.
- Admin role endpoints under `/api/admin/sticker-packs` create/update/soft-disable packs and stickers. Pack creation accepts a required JPEG/PNG/WebP thumbnail as multipart data; sticker uploads remain PNG/WebP. Both use `viora/stickers/{packId}` on the existing Cloudinary account.

## Client

- Shared React Native/Web panel loads usable packs from API, searches, switches recent/pack tabs, and opens the store.
- Selecting a sticker sends immediately through the existing optimistic/realtime chat flow; panel remains open.
- Dedicated transparent contained sticker rendering; no image-message card/crop.
- Recent list: max 24, MRU, deduplicated; localStorage on Web and AsyncStorage on native.
- Store: featured/free/paid/owned tabs, cards, preview grid, ownership/pricing state. Paid action is visibly disabled until ANKT wallet integration exists.

## Admin

- New “Nhãn dán” navigation route.
- Pack list shows thumbnail, count, price, owners, usage, state.
- Pack form selects a local thumbnail for creation/update, uploads it through the backend to Cloudinary, and has no manual sort-order field.
- Sticker form supports PNG/WebP upload, name, URL, order-on-create, and active/inactive.

## Files

### Backend

- `Viora.Domain/Entities/StickerEntities.cs`
- `Viora.Domain/Entities/MessagingEntities.cs`
- `Viora.Domain/Entities/Enums.cs`
- `Viora.Application/Stickers/StickerContracts.cs`
- `Viora.Application/Stickers/AdminStickerContracts.cs`
- `Viora.Application/Chat/ChatContracts.cs`
- `Viora.Application/Chat/ChatHandlers.cs`
- `Viora.Application.Tests/Chat/StickerMessageValidatorTests.cs`
- `Viora.Infrastructure/Persistence/Configurations/StickerConfigurations.cs`
- `Viora.Infrastructure/Persistence/Configurations/MessagingConfigurations.cs`
- `Viora.Infrastructure/Persistence/Repositories/StickerService.cs`
- `Viora.Infrastructure/Persistence/Repositories/AdminStickerService.cs`
- `Viora.Infrastructure/Persistence/Repositories/ChatConversationRepository.cs`
- `Viora.Infrastructure/Persistence/AppDbContext.cs`
- `Viora.Infrastructure/Persistence/Migrations/20260908045331_AddStickerSystem.cs`
- `Viora.Infrastructure/Persistence/Migrations/20260908045331_AddStickerSystem.Designer.cs`
- `Viora.Infrastructure/Persistence/Migrations/AppDbContextModelSnapshot.cs`
- `Viora.Infrastructure/Media/CloudinaryMediaStorage.cs`
- `Viora.Infrastructure/DependencyInjection.cs`
- `viora-BE/Controllers/StickerPacksController.cs`
- `viora-BE/Controllers/Admin/AdminStickerPacksController.cs`
- `viora-BE/Controllers/ChatController.cs`
- `docs/specs/sticker-system.md`
- `docs/plans/sticker-system.md`

### App/Web

- `types/sticker.ts`, `types/chat.ts`
- `services/sticker.service.ts`, `services/chat.service.ts`, `services/chat-foreground-notification.service.ts`
- `features/stickers/sticker-panel.tsx`
- `features/stickers/recent-stickers.ts`, `features/stickers/recent-sticker-storage.ts`
- `features/stickers/recent-stickers.test.mjs`, `features/stickers/sticker-chat-contract.test.mjs`
- `features/chat/chat-screen.tsx`, `features/chat/chat.mapper.ts`, `features/chat/chat-send-status.test.mjs`
- `utils/conversation-list.ts`, `scripts/test.mjs`
- `app/sticker-store.tsx`

### Admin

- `src/types/sticker.ts`
- `src/services/admin-sticker.service.ts`
- `src/pages/StickerPacksPage.tsx`
- `src/App.tsx`, `src/layouts/AdminLayout.tsx`, `src/ankt-admin.css`
- `tests/stickers.test.ts`

## Verification

- Backend build: PASS (0 warnings/errors).
- Backend tests: PASS (49/49).
- App TypeScript/lint: PASS.
- App tests: PASS (177/177).
- Admin TypeScript/lint/build: PASS.
- Admin tests: PASS (existing 10 assertions plus sticker route test).
- Migration model parity: PASS; live apply intentionally not run.
- Free sticker/message/realtime/ownership code paths: implemented and build/test verified; live E2E requires migrated DB, configured Cloudinary, seeded/uploaded pack, and authenticated clients.
- Paid purchase, balance debit, rollback, double-purchase: BLOCKED by missing ANKT wallet boundary.

## Required Next Input

Provide the existing ANKT wallet/coin ledger location or contract (entity/service/API and transaction semantics). The paid purchase slice can then atomically debit the database price, record its real wallet transaction ID, create purchase/ownership, and prove concurrency behavior without introducing a second money system.
