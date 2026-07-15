# Notifications API Handoff

## Scope

Implemented authenticated notification APIs:

- `GET /api/notifications`
- `PUT /api/notifications/{id}/read`
- `PUT /api/notifications/read-all`

## Architecture

- Application contracts/handlers: `Viora.Application/Notifications`
- Notification creation factory: `Viora.Application/Notifications/NotificationFactory.cs`
- EF repository: `Viora.Infrastructure/Persistence/Repositories/NotificationRepository.cs`
- API controller: `viora-BE/Controllers/NotificationsController.cs`
- DI registration: `Viora.Infrastructure/DependencyInjection.cs`
- Contract tests: `Viora.Infrastructure.Tests/NotificationApiContractTests.cs`
- Factory tests: `Viora.Infrastructure.Tests/NotificationFactoryTests.cs`

## Behavior

- Uses JWT `user_id` claim and returns only current user's notifications.
- Supports `page`, `pageSize`, `isRead`, and `type` filters.
- Sorts by `CreatedAt DESC`, then `Id DESC` for deterministic paging.
- Projects only required DTO fields, including nullable sender/reference.
- System notifications return `Sender = null`.
- `unreadCount` counts all unread notifications for the current user, independent of list filters.
- Mark-one-read is idempotent for already-read notifications and returns 404 if the notification is not owned by the user.
- Mark-all-read updates only unread notifications for the current user and returns `UpdatedCount`.
- All application notification creation should go through `NotificationFactory`; handlers should not create `Notification` directly.
- `Title` is a category label, while `Content` is the display sentence.
- `ImageUrl` defaults to null and should only be set for banner/product/event-style notification images.
- Post interaction notifications use `PostType.Post` as "bài viết" and `PostType.ShortVideo` as "video".

## Verification Notes

- `dotnet build Viora.Application/Viora.Application.csproj --no-restore -v minimal` passed.
- `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore --filter NotificationFactoryTests -v minimal /p:BuildProjectReferences=false` passed.
- Full test/build may fail while the running `viora-BE` process locks DLLs under `viora-BE/bin`.
