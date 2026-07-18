# Handoff: Chat Conversations List

Implemented `GET /api/chat/conversations`.
Implemented `GET /api/chat/conversations/{conversationId}/messages`.
Implemented `POST /api/chat/messages`.
Implemented `POST /api/chat/attachments/upload`.
Implemented `POST /api/chat/messages/{messageId}/recall`.
Implemented `POST /api/chat/conversations/{conversationId}/read`.
Implemented `PATCH /api/chat/conversations/{conversationId}/pin`.
Implemented `PATCH /api/chat/conversations/{conversationId}/mute`.
Implemented `GET /api/chat/conversations/{conversationId}`.
Private items returned by `GET /api/chat/conversations` include the other participant's verification, stranger, and friendship state. Group items keep `otherParticipant = null`; friendship data is projected in the conversation query without per-item database calls.
Implemented `POST /api/chat/conversations/private` to get or create a private conversation. The operation validates the recipient's messaging setting for new conversations and uses a PostgreSQL transaction advisory lock per normalized user pair to prevent concurrent duplicates.
Implemented `GET /api/chat/conversations/{conversationId}/attachments`.
Implemented `GET /api/chat/conversations/{conversationId}/links`.
Implemented `GET /api/chat/conversations/{conversationId}/search`.
Implemented `PATCH /api/chat/conversations/{conversationId}/block`.

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
- Send message rejects non-HTTPS attachment URLs, including Expo/local `file://` paths.
- Chat attachment upload uses Cloudinary and returns public `https://` URLs. Image uses image upload, video/audio use video upload, other files use raw upload.
- FE flow: upload local files with multipart `files`, then pass returned attachment objects into `POST /api/chat/messages`.
- Send message checks active membership, group send permission, conversation block, and reply message belongs to the same conversation.
- Message insert, attachments insert, and conversation last-message update run in one transaction.
- After commit, handler emits SignalR `ReceiveMessage` to active conversation members with the API response payload.
- Push is best-effort through existing FCM sender for offline recipients based on connection registry; the app does not yet track "currently opened conversation" state.
- Mark-read adds nullable `ConversationMembers.LastReadAt` via migration `20260717033800_AddConversationMemberLastReadAt`.
- Mark-read uses `Conversations.LastMessageId` to avoid scanning large message tables.
- Mark-read updates only when the current member's `LastReadMessageId` differs from the conversation last message.
- Mark-read returns success with `lastReadMessageId = null` for empty conversations and does not update data in that case.
- Mark-read emits `MessagesRead` to active members only when the read pointer was updated.
- Chat realtime event names are centralized in `RealtimeEvents` and include the FE contract names.
- Send-message realtime is now per recipient:
  - `ReceiveMessage` has recipient-specific `isMine`.
  - `ConversationUpdated` has recipient-specific `unreadCount` and last-message `isMine`.
  - `NewMessageNotification` is not sent to the sender and is skipped for muted conversations.
  - `MessageDelivered` is sent to the sender after commit.
- Mark-read emits `ConversationRead` and legacy `MessagesRead`, plus `ConversationUpdated` for the reader.
- `MessageAttachments.ThumbnailUrl` was added by migration `AddMessageAttachmentThumbnailUrl` and is included in attachment payloads.
- Recall message only allows the original sender while still an active conversation member.
- Recall updates message to `MessageType.Recall`, `IsDeleted = true`, and hides attachments in message history/list lastMessage.
- Recall emits `MessageDeleted` to active members and then per-user `ConversationUpdated`.
- Pin/unpin validates active membership, updates only the current user's `ConversationMembers.IsPinned` via `ExecuteUpdateAsync`, and emits `ConversationPinnedChanged` only to that user.
- Mute/unmute validates active membership, updates only the current user's `ConversationMembers.IsMuted` via `ExecuteUpdateAsync`, and emits `ConversationMutedChanged` only to that user.
- Conversation info validates active membership and returns current user's `isPinned`, `isMuted`, and `isBlocked`.
- Private conversation info uses the other active member for `name` and `avatarUrl`; group info uses `Conversations.Name`/`AvatarUrl` and includes `CanSendMessage` plus `CreatedBy`.
- Shared attachments are projected from `MessageAttachments` through messages in the same conversation, newest-first, with `type` filter: `0 All`, `1 Image`, `2 Video`, `3 File`, `4 Audio`.
- Shared links are extracted from message content with an HTTP/HTTPS regex and returned paged newest-first.
- Message search uses the same PostgreSQL `translate(lower(...))` accent-insensitive pattern as conversation keyword search.
- Block/unblock is private-conversation only, creates/deletes `ConversationBlocks`, and emits `ConversationBlockedChanged` only to the current user.

Verification:
- `dotnet build viora-BE.sln --no-restore -v:minimal` passed.
- `dotnet test Viora.Infrastructure.Tests\Viora.Infrastructure.Tests.csproj --no-restore -v:minimal --filter "ChatApiContractTests|RealtimeApiContractTests"` passed 33 tests.

Note:
- Build/test emitted `NU1900` warnings because NuGet vulnerability feed was unavailable; compilation and tests still succeeded.
