# Realtime Notifications Handoff

Implemented backend foundation:

- Shared SignalR hub at `/hubs/realtime`.
- JWT auth for SignalR via `access_token` query on `/hubs/realtime`.
- `IRealtimeService` wraps SignalR user/group sends.
- `IConnectionRegistry` tracks many connections per user.
- `DeviceTokens` entity/table with unique token and active flag.
- `POST /api/device-token/register` upserts token and moves it to the current user.
- `POST /api/device-token/unregister` marks the token inactive.
- `INotificationService` creates a notification, saves it, sends `ReceiveNotification`, then calls push sender.
- `IPushNotificationSender` is wired to `NoOpPushNotificationSender` until Firebase Admin SDK and credentials are added.
- Frontend integration docs live in `docs/realtime-notifications.md`.

Verification:

- `dotnet build viora-BE.sln --no-restore --disable-build-servers -maxcpucount:1 --verbosity minimal` passes.
- Realtime/device-token/persistence focused tests pass.
- Full suite still fails on unrelated existing Swagger logout auth expectation.

Next work:

- Add Firebase Admin SDK package and a real FCM sender.
- Decide whether to migrate existing notification creation paths to `INotificationService`.
- Add message send/edit/read handlers to publish through `IRealtimeService`.
