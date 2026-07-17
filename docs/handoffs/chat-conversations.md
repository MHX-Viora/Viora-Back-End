# Handoff: Chat Conversations List

Implemented `GET /api/chat/conversations`.
Implemented `GET /api/chat/conversations/{conversationId}/messages`.
Implemented `POST /api/chat/messages`.

Key files:
- `Viora.Application/Chat/ChatContracts.cs`
- `Viora.Application/Chat/ChatHandlers.cs`
- `Viora.Infrastructure/Persistence/Repositories/ChatConversationRepository.cs`
- `viora-BE/Controllers/ChatController.cs`
- `Viora.Infrastructure.Tests/ChatApiContractTests.cs`

Behavior:
- Requires `[Authorize]`; user id is read from JWT claim `user_id`.
- Query defaults: `page = 1`, `pageSize = 20`; handler clamps `page >= 1`, `pageSize <= 50`.
- Filters conversations through current user's `ConversationMember` with `Status == Active`.
- Private display name/avatar comes from the other active member.
- Group display name/avatar comes from `Conversation`.
- Unread count uses `ConversationMembers.LastReadMessageId` and counts messages from other users after that message's `CreatedAt`.
- Keyword search uses PostgreSQL built-in `translate(lower(...))` plus client-side keyword normalization, avoiding a DB extension.
- Message history defaults: `page = 1`, `pageSize = 30`; handler clamps `page >= 1`, `pageSize <= 100`.
- Message history returns 404 for missing conversation and 403 when the current user is not an active member.
- Message history queries messages by `CreatedAt DESC` for paging, then reverses the page so response items are oldest-to-newest.
- Reactions are loaded once for the current page's message ids, then grouped in memory for reaction lists and summaries.
- Send message reads sender id from JWT only; request has no sender field.
- Send message validates message type rules and rejects client-created `Recall`.
- Send message checks active membership, group send permission, conversation block, and reply message belongs to the same conversation.
- Message insert, attachments insert, and conversation last-message update run in one transaction.
- After commit, handler emits SignalR `ReceiveMessage` to active conversation members with the API response payload.
- Push is best-effort through existing FCM sender for offline recipients based on connection registry; the app does not yet track "currently opened conversation" state.

Verification:
- `dotnet build viora-BE.sln --no-restore -v:minimal` passed.
- `dotnet test Viora.Infrastructure.Tests\Viora.Infrastructure.Tests.csproj --no-restore -v:minimal --filter "ChatApiContractTests|SendChatMessageValidatorTests"` passed 15 tests.

Note:
- Build/test emitted `NU1900` warnings because NuGet vulnerability feed was unavailable; compilation and tests still succeeded.
