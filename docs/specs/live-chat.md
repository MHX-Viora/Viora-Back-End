# Transient Live chat

## Objective
Ordinary Live comments are short lived SignalR events. A thread-safe per-Live buffer holds at most 200 comments and supplies the latest 100 in `JoinLive`. PostgreSQL retains Live totals, gifts, and report evidence, but receives no ordinary comment row.

## Commands and structure
- Build: `dotnet build viora-BE.sln --no-restore`
- Tests: `dotnet test Viora.Application.Tests/Viora.Application.Tests.csproj --no-restore --filter FullyQualifiedName~Live`
- Backend contracts: `Viora.Application/Live`; implementation: `Viora.Infrastructure/Live`; hub: `Viora.Infrastructure/Realtime/RealtimeHub.cs`.
- Client: `viora/services/live-realtime.service.ts` and existing host/viewer screens.

## Behavior and boundaries
- Live must be active, user authorized, text 1–500 characters, and user not muted or blocked. Limit each user to five comments per five seconds in a Live.
- `JoinLive` joins `live:{id:N}` before returning a recent snapshot. Clients merge by event ID to avoid duplicates during join/reconnect.
- Host/moderator deletion removes a buffered comment and emits `LiveCommentDeleted` to that group. Reporting writes a snapshot into the existing Reports system.
- A batcher flushes `TotalComments` periodically and on normal Live end. Restart can lose recent comments and unflushed counts. No Redis/backplane is added.
- Existing `LiveComments` schema and rows remain untouched. Gift transactions, wallet, payment, and Agora code remain unchanged.
- No normal Live comment reads, writes, updates, deletes, or counts the `LiveComments` table.

## Verification
Concurrency, trimming, isolation, rate limiting, deletion, end lifecycle, report evidence, client deduplication and bounded state have focused tests; build and type checks pass.
