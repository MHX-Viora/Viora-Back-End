# Realtime Notifications Handoff

Implemented backend foundation:

- Shared SignalR hub at `/hubs/realtime`.
- JWT auth for SignalR via `access_token` query on `/hubs/realtime`.
- `IRealtimeService` wraps SignalR user/group sends.
- `IConnectionRegistry` tracks many connections per user.
- `DeviceTokens` table stores device rows with unique `Token`, unique nullable `DeviceId`, and active flag.
- `POST /api/device-token/register` upserts token/device id and moves the device to the current user.
- `POST /api/device-token/unregister` marks the token inactive.
- `/api/device/register` and `/api/device/unregister` remain compatible aliases.
- `INotificationService` creates a notification, saves it, sends `ReceiveNotification`, then calls push sender.
- `IPushNotificationSender` is wired to `FirebasePushNotificationSender`.
- Firebase config supports `Firebase:ServiceAccountJson` or `Firebase:ServiceAccountPath`.
- Android FCM messages include `Notification`, data, high priority, channel id `default`, and default sound.
- Invalid/unregistered FCM tokens are marked inactive.
- Realtime and FCM data include the full notification item shape used by `GET /api/notifications`.
- Frontend integration docs live in `docs/realtime-notifications.md`.

Verification:

- `dotnet build viora-BE.sln --no-restore --disable-build-servers -maxcpucount:1 --verbosity minimal` passes.
- Realtime/device-token/persistence focused tests pass.
- Full suite still fails on unrelated existing Swagger logout auth expectation.

Next work:

- Decide whether to migrate existing notification creation paths to `INotificationService`.
- Add message send/edit/read handlers to publish through `IRealtimeService`.
