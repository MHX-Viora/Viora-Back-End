# Spec: Chat Conversations List

## Objective
Add chat read APIs for the authenticated user:
- `GET /api/chat/conversations` lists active conversations with pagination, keyword search, last message, unread count, mute, and pin metadata.
- `GET /api/chat/conversations/{conversationId}/messages` lists paged message history.
- `POST /api/chat/messages` sends a message as the authenticated user.
- `POST /api/chat/attachments/upload` uploads chat files to public storage before sending a message.
- `POST /api/chat/messages/{messageId}/recall` recalls a sent message.
- `POST /api/chat/conversations/{conversationId}/read` marks the authenticated user's active conversation membership as read.

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
- Always: reject attachment `fileUrl` values that are not public HTTPS URLs. FE must upload local files before sending messages.
- Always: for mark-read, update only the current active membership row and emit `MessagesRead` only when the read pointer changes.
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
- Attachment upload accepts multipart `files` and returns public URLs plus metadata for `POST /api/chat/messages`.
- Recall marks the message as deleted, changes `MessageType` to `Recall`, hides old attachments in read APIs, emits `MessageDeleted`, and sends `ConversationUpdated`.
- Mark-read stores `ConversationMembers.LastReadMessageId` and nullable `LastReadAt`; it does not create `MessageReads`.

## Realtime Events
Canonical event names are defined in `RealtimeEvents`.

Implemented by current chat APIs:
- `ReceiveMessage`: emitted per active member after `POST /api/chat/messages`; `isMine` is calculated per recipient.
- `ConversationUpdated`: emitted per active member after a new message and to the reader after mark-read.
- `NewMessageNotification`: emitted to recipients other than the sender when the conversation is not muted.
- `MessageDelivered`: emitted to the sender after the message is committed.
- `ConversationRead`: emitted after read pointer changes.
- `MessagesRead`: emitted as a backward-compatible alias for `ConversationRead`.

Reserved event names for related future APIs:
- `ConversationCreated`
- `MessageRead`
- `MessageDeleted`
- `MessageUpdated`
- `ReactionAdded`
- `ReactionRemoved`
- `TypingStarted`
- `TypingStopped`
- `UserOnline`
- `UserOffline`
- `ConversationPinned`
- `ConversationMuted`
- `ConversationRenamed`
- `ConversationAvatarChanged`
- `MemberAdded`
- `MemberRemoved`
- `MemberLeft`
