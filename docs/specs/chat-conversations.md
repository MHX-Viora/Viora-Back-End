# Spec: Chat Conversations List

## Objective
Add chat read APIs for the authenticated user:
- `GET /api/chat/conversations` lists active conversations with pagination, keyword search, last message, unread count, mute, and pin metadata.
- `GET /api/chat/conversations/{conversationId}/messages` lists paged message history.
- `POST /api/chat/messages` sends a message as the authenticated user.

## Tech Stack
ASP.NET Core controller, MediatR query handler, EF Core/Npgsql projection repository.

## Commands
- Build: `dotnet build viora-BE.sln --no-restore -v:minimal`
- Test: `dotnet test Viora.Infrastructure.Tests\Viora.Infrastructure.Tests.csproj --no-restore -v:normal --filter ChatApiContractTests`

## Project Structure
- `Viora.Application/Chat/` defines query, response contracts, handler, and repository interface.
- `Viora.Infrastructure/Persistence/Repositories/` implements EF query projection.
- `viora-BE/Controllers/` exposes the authenticated HTTP endpoint.
- `Viora.Infrastructure.Tests/` validates API contract shape.

## Code Style
```csharp
public sealed record GetChatConversationsQuery(
    Guid UserId,
    int Page,
    int PageSize,
    string? Keyword) : IRequest<ChatConversationListResponse>;
```

Use sealed records for response contracts, MediatR handlers for orchestration, and repository projections without `Include`.

## Testing Strategy
Contract tests verify route, authorization, query defaults, response fields, and enum usage. Build validates EF expression compilation.

## Boundaries
- Always: derive current user from JWT `user_id`; clamp `page` to at least 1 and `pageSize` to at most 50.
- Always: for message history, return 404 when the conversation does not exist and 403 when the viewer is not an active member.
- Always: for sending, reject client-supplied sender identity, reject `Recall`, validate payload by `MessageType`, and send realtime only after transaction commit.
- Ask first: database schema changes, new dependencies, or endpoint contract changes.
- Never: accept `UserId` from query/body for this API.

## Success Criteria
- Only active memberships are returned.
- Private conversations display the other active member's name/avatar.
- Group conversations display `Conversations.Name` and `Conversations.AvatarUrl`.
- Results sort by `LastMessageAt`, falling back to `CreatedAt`, descending.
- Keyword search is case-insensitive and Vietnamese accent-insensitive.
- Message history is fetched newest-first for paging, then returned oldest-to-newest within the page.
- Message history includes sender, reply preview, attachments, reactions, reaction summary, edit/delete flags, and ownership flag.
- Sending creates `Messages` and `MessageAttachments`, updates `Conversations.LastMessageId` and `LastMessageAt`, and emits `ReceiveMessage` with the API response payload.
