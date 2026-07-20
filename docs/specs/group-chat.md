# Spec: Group chat backend

## System messages

- Group mutations persist a `Messages` row with `MessageType = 100` and Vietnamese text in `Content`.
- They use the actor as `SenderUserId`, have no attachments, and appear in the existing message-history pagination by creation time.
- Clients cannot create system messages through the send-message API.
- System messages cannot be replied to, edited, recalled, or reacted to.
- Existing stored system messages using the legacy value `8` are migrated to `100`.

## Message forwarding

- `POST /api/chat/messages/{messageId}/forward` accepts one to twenty distinct `conversationIds` and returns `{ "success": true }`.
- The authenticated user must be an active member of the source and every destination, and must currently be allowed to send to every destination.
- System, recalled, and deleted messages cannot be forwarded.
- Each destination receives a new `Messages` row with a new ID, the forwarding user as sender, copied content/type/reply reference, and newly created attachment rows that reuse the existing file URLs.
- Creation is atomic across destinations. Normal receive-message, conversation-update, notification, delivery, and push realtime flows run after commit.

## Objective

Add authenticated group-chat APIs without breaking private chat. Reuse `Conversations`,
`ConversationMembers`, `Messages`, and `Notifications`; groups use
`ConversationType.Group`.

## Contracts

- `GET /api/friends/selectable`: accepted, active friends; display-name search;
  pagination; online users first, then display name.
- `POST /api/chat/groups`: multipart group creation with optional image and at
  least two distinct accepted friends.
- `GET /api/chat/groups/{conversationId}` and `/members`: active group members
  can read group details and the paginated member list.
- Member mutations: add, remove, leave, promote, demote, and transfer ownership.
- Group mutations: rename, replace avatar, change send permission, and dissolve.

Mutation responses follow existing `ProblemDetails` errors and emit the relevant
SignalR events after persistence. Invite and role-change operations create existing
group notification types. Group activities are stored as `MessageType.System`.

## Decisions

- Group names are trimmed and limited to the existing database limit of 100 chars.
- Group avatars accept JPEG, PNG, or WebP up to 5 MiB and use Cloudinary.
- A removed/left member can be re-added by reactivating the existing composite-key row.
- Dissolution is soft deletion via `Conversation.DeletedAt`; member history and the
  final system message remain available for audit, but the group is no longer usable.
- All data mutations run in database transactions. External realtime/push delivery
  occurs after commit to avoid announcing rolled-back state.

## Commands and structure

- Build: `dotnet build viora-BE.sln --no-restore`
- Test: `dotnet test viora-BE.sln --no-restore`
- API: `viora-BE/Controllers`
- Contracts/handlers: `Viora.Application/Chat`, `Viora.Application/Social`
- Persistence: `Viora.Infrastructure/Persistence/Repositories`
- Tests: `Viora.Infrastructure.Tests`

## Testing strategy

Use xUnit contract/validator tests for routes, payloads, authorization boundaries,
and validation. Exercise repository rules with the existing persistence test style,
then run the complete solution test suite and build.

## Boundaries

- Always: derive the actor from JWT, check active group membership and role, validate
  all IDs/files/enums, and keep mutations atomic.
- Never: trust an actor ID supplied by the client, expose inactive users, create a
  second group schema, or change private-chat contracts.
- No new table is introduced.

## Success criteria

- All 14 requested endpoints return the documented shapes/statuses.
- Owner/Admin/Member permissions and owner-leave rules are enforced server-side.
- System messages, notifications, and realtime events are produced where specified.
- Existing private chat tests and APIs continue to pass.
