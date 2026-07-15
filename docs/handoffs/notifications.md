# Notifications API Handoff

## Scope

Implemented authenticated notification APIs:

- `GET /api/notifications`
- `PUT /api/notifications/{id}/read`
- `PUT /api/notifications/read-all`

## Architecture

- Application contracts/handlers: `Viora.Application/Notifications`
- EF repository: `Viora.Infrastructure/Persistence/Repositories/NotificationRepository.cs`
- API controller: `viora-BE/Controllers/NotificationsController.cs`
- DI registration: `Viora.Infrastructure/DependencyInjection.cs`
- Contract tests: `Viora.Infrastructure.Tests/NotificationApiContractTests.cs`
- Post/video notification text factory: `Viora.Application/Notifications/PostNotificationFactory.cs`

## Behavior

- Uses JWT `user_id` claim and returns only current user's notifications.
- Supports `page`, `pageSize`, `isRead`, and `type` filters.
- Sorts by `CreatedAt DESC`, then `Id DESC` for deterministic paging.
- Projects only required DTO fields, including nullable sender/reference.
- System notifications return `Sender = null`.
- `unreadCount` counts all unread notifications for the current user, independent of list filters.
- Mark-one-read is idempotent for already-read notifications and returns 404 if the notification is not owned by the user.
- Mark-all-read updates only unread notifications for the current user and returns `UpdatedCount`.
- Post interaction notifications are generated through `PostNotificationFactory`, so `PostType.Post` uses "bài viết" text and `PostType.ShortVideo` uses "video" text.
- Post/video like, comment, reply, mention, and share notification text should be changed in the factory only.

## Verification Notes

- `dotnet build Viora.Application/Viora.Application.csproj --no-restore -v minimal` passed.
- `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore --no-build -v normal` ran the previous build and failed one pre-existing Swagger Logout test unrelated to this change.
- Full restore/build for Infrastructure failed before compiler diagnostics with 0 errors/warnings in this environment, likely SDK/MSBuild restore issue.
