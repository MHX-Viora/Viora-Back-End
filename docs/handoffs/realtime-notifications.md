# Realtime Notifications Handoff

Implemented backend foundation:

- Shared SignalR hub at `/hubs/realtime`.
- JWT auth for SignalR via `access_token` query on `/hubs/realtime`.
- `IRealtimeService` wraps SignalR user/group sends.
- `IConnectionRegistry` tracks many connections per user.
- `UserDevices` table stores device rows with unique `FcmToken`, unique nullable `DeviceId`, and active flag.
- `POST /api/device/register` upserts token/device id and moves the device to the current user.
- `POST /api/device/unregister` marks the token inactive.
- Old `/api/device-token/register` and `/api/device-token/unregister` routes remain backward compatible.
- `INotificationService` creates a notification, saves it, sends `ReceiveNotification`, then calls push sender.
- `IPushNotificationSender` is wired to `FirebasePushNotificationSender`.
- Firebase config supports `Firebase:ServiceAccountJson` or `Firebase:ServiceAccountPath`.
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
