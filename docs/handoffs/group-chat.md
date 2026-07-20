# Group chat backend handoff

## Delivered

- `GET /api/friends/selectable` with accepted-friend filtering, search, paging,
  online-first ordering, and last-login fallback for `lastActiveAt`.
- All requested `/api/chat/groups` create/read/member/role/settings/leave/dissolve APIs.
- Group mutations use transactions, system messages (`MessageType.System = 8`),
  existing group notification types, and SignalR user delivery.
- Group avatar uploads reuse Cloudinary with JPEG/PNG/WebP and 5 MiB validation.
- Dissolution uses `Conversations.DeletedAt`; migration:
  `20260720030653_AddGroupConversationSoftDelete`.
- SignalR conversation group joining now verifies active membership.
- Public send-message validation rejects forged System messages.

## Verification

- Build: `dotnet build viora-BE/viora-BE.csproj --no-restore` passed with zero warnings.
- Focused chat tests: 34/34 passed.
- Group chat contract tests: 5/5 passed.
- Full suite: 176 passed, 1 unrelated existing Swagger authorization test failed
  because it expects `AccountsController.Logout` to be public while the action has
  `[Authorize]`; no account/auth file was changed by this feature.

## Deployment

Apply the EF migration before exposing the endpoints:

```powershell
dotnet ef database update --project Viora.Infrastructure\Viora.Infrastructure.csproj --startup-project viora-BE\viora-BE.csproj
```

## Follow-up testing

Run API integration tests against PostgreSQL for concurrent membership/role mutations
and verify Cloudinary, push notification, and SignalR delivery with configured services.

