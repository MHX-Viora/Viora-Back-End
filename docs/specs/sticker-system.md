# Spec: ANKT Sticker System

## Objective

Add database-driven static PNG/WebP sticker packs to the existing ANKT chat, SignalR, Cloudinary, mobile/web app, and admin. Free packs are usable by everyone; paid packs are usable only by owners. Ownership controls sending, never viewing historical messages.

## Existing Stack

- Backend: .NET 8, MediatR, EF Core 8, PostgreSQL/Npgsql, SignalR, Cloudinary.
- Client: Expo/React Native 0.81 with React Native Web and AsyncStorage.
- Admin: React 19, Vite, React Query, Axios.
- Existing `MessageType.Sticker = 5` is retained. `Message` is extended with nullable `StickerId`.

## Commands

- Backend build: `dotnet build viora-BE.sln --no-restore`
- Backend tests: `dotnet test viora-BE.sln --no-restore`
- Migration: `dotnet ef migrations add AddStickerSystem --project Viora.Infrastructure --startup-project viora-BE`
- App tests: `npm test`; lint: `npm run lint`
- Admin tests: `npm test`; build: `npm run build`; lint: `npm run lint`

## Project Structure

- `Viora.Domain/Entities`: sticker, ownership, purchase, and message relationships.
- `Viora.Application/Stickers`: public/admin contracts and service boundaries.
- `Viora.Infrastructure/Persistence`: EF configuration and service implementation.
- `viora-BE/Controllers`: authenticated public and admin HTTP boundaries.
- `viora/features/chat`, `viora/features/stickers`: shared native/web picker, store, recent, rendering.
- `viora-admin/src`: pack and sticker management.

## API Contract

- `GET /api/sticker-packs?type=featured|free|paid|owned&page=1&pageSize=20`
- `GET /api/sticker-packs/{id}`
- `POST /api/sticker-packs/{id}/purchase` (requires the existing ANKT coin provider)
- Existing `POST /api/chat/messages` gains optional `stickerId`; Sticker messages require it and forbid content/attachments.
- Admin CRUD is additive under `/api/admin/sticker-packs`; delete actions are soft-disable operations.
- Sticker DTOs always include render URLs. Messages persist only `StickerId`.

## Data Rules

- `Price = 0` is the single source of truth for free status; no duplicate `IsFree` column.
- Unique ownership key: `(UserId, StickerPackId)`.
- Pack and sticker removal is soft (`IsActive = false`).
- `Sticker -> Message` and user ownership relationships use restrictive delete behavior.
- Availability uses UTC nullable bounds.
- Static formats in v1: PNG and WebP; URL only, never binary/base64.

## Testing Strategy

- Unit tests first for message validation, availability, ownership, and recent ordering.
- Integration-shaped EF tests where current test infrastructure permits it.
- Contract tests for client/admin routing and request shapes.
- Build all three repositories; manually verify realtime payload and responsive rendering when a runnable API/database is available.

## Boundaries

- Always: server-derived user/price/ownership; conversation authorization; bounded queries; atomic writes; historical renderability.
- Ask first: inventing or replacing ANKT wallet/coin storage, auth changes, new external storage/dependencies.
- Never: hard-code packs in clients, create another chat/hub/wallet, store image URLs per message, hard-delete sent stickers, reset data or old migrations.

## Success Criteria

- Free sticker catalog, picker, immediate send, realtime receive, reload rendering, recent persistence, and admin management work on native/web.
- Paid catalog and ownership model are ready; purchase becomes active only through the real ANKT wallet transaction boundary and is concurrency-safe.
- No N+1 ownership checks; required indexes and restrictive foreign keys exist.
- Existing behavior and user changes remain intact.

## Open Question

- The repository contains no Wallet/Balance/Coin or WalletTransaction implementation. Which existing ANKT service/database is the authoritative coin ledger? Paid purchase cannot safely debit funds until that boundary is supplied; creating a replacement wallet is explicitly out of scope.
