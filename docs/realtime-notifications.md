# FE Guide: Realtime + Push Notifications

This document explains how the frontend connects to Viora realtime events and registers FCM device tokens.

## Base URLs

REST API:

```text
{API_URL}/api
```

SignalR hub:

```text
{API_URL}/hubs/realtime
```

All authenticated REST calls use:

```http
Authorization: Bearer <access_token>
```

SignalR uses the same access token through `accessTokenFactory`.

## Install

React Native / Expo:

```bash
npm install @microsoft/signalr
```

Bare React Native FCM packages depend on the app setup. Common choices:

```bash
npm install @react-native-firebase/app @react-native-firebase/messaging
```

Web:

```bash
npm install @microsoft/signalr
```

## Connect To SignalR

Create one shared connection for the logged-in session. Do not create a new connection per screen.

```ts
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";

let connection: HubConnection | null = null;

export function createRealtimeConnection(apiUrl: string, getAccessToken: () => string) {
  connection = new HubConnectionBuilder()
    .withUrl(`${apiUrl}/hubs/realtime`, {
      accessTokenFactory: getAccessToken,
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Information)
    .build();

  return connection;
}

export async function startRealtime() {
  if (!connection) throw new Error("Realtime connection is not created.");
  if (connection.state === HubConnectionState.Disconnected) {
    await connection.start();
  }
}

export async function stopRealtime() {
  if (connection && connection.state !== HubConnectionState.Disconnected) {
    await connection.stop();
  }
}
```

Call `startRealtime()` after login and after you have a valid access token. Call `stopRealtime()` on logout.

## Token Refresh

SignalR reads the token from `accessTokenFactory` when connecting/reconnecting. Keep `getAccessToken()` pointed at the latest token in memory/storage.

If the backend returns `401` or token refresh happens while SignalR is connected:

```ts
await stopRealtime();
await startRealtime();
```

## Register FCM Device Token

Register after login, after FCM permission is granted, and whenever FCM rotates the token.

```http
POST /api/device-token/register
Authorization: Bearer <access_token>
Content-Type: application/json
```

Request:

```json
{
  "token": "FCM_TOKEN",
  "deviceId": "device_unique_id",
  "deviceName": "Samsung S24",
  "platform": 0,
  "appVersion": "1.0.0"
}
```

Response:

```json
{
  "success": true,
  "isActive": true
}
```

Platform values:

| Value | Platform |
|---:|---|
| `0` | Android |
| `1` | iOS |
| `2` | Web |
| `3` | Other |

Example:

```ts
export async function registerDeviceToken(apiUrl: string, accessToken: string, fcmToken: string) {
  const response = await fetch(`${apiUrl}/api/device-token/register`, {
    method: "POST",
    headers: {
      Authorization: `Bearer ${accessToken}`,
      "Content-Type": "application/json",
    },
    body: JSON.stringify({
      token: fcmToken,
      deviceId: "device_unique_id",
      deviceName: "Samsung S24",
      platform: 0,
      appVersion: "1.0.0",
    }),
  });

  if (!response.ok) throw new Error("Failed to register device token.");
  return response.json();
}
```

Backend behavior:

- `token` is unique.
- If the token already exists, backend moves it to the current user.
- `isActive = true`.
- `lastSeenAt` is updated.
- No duplicate token rows are created.

## Unregister FCM Device Token

Call this on logout, or when the app decides this device should no longer receive push for the current account.

```http
POST /api/device-token/unregister
Authorization: Bearer <access_token>
Content-Type: application/json
```

Request:

```json
{
  "token": "FCM_TOKEN"
}
```

Response:

```json
{
  "success": true,
  "isActive": false
}
```

Backend only marks the token inactive. It does not delete the row.

## Event Names

Backend event names are stable strings:

```text
ReceiveNotification
ReceiveMessage
MessageEdited
MessageDeleted
MessagesRead
ConversationUpdated
ConversationCreated
FriendRequestReceived
FriendRequestAccepted
UserFollowed
TypingStarted
TypingStopped
UserOnline
UserOffline
```

## TypeScript Types

Use these client-side types.

```ts
export type NotificationReferenceType =
  | 0 // User
  | 1 // Post
  | 2 // Comment
  | 3 // Conversation
  | 4 // Message
  | 5; // Identity

export type NotificationPayload = {
  notificationId: string;
  notificationType: number;
  referenceId: string | null;
  referenceType: NotificationReferenceType | null;
  title: string;
  content: string | null;
  imageUrl: string | null;
  createdAt: string;
};

export type RealtimeMessagePayload = Record<string, unknown>;
export type ConversationPayload = Record<string, unknown>;
export type TypingPayload = Record<string, unknown>;
export type PresencePayload = Record<string, unknown>;
```

Only `ReceiveNotification` has a finalized payload in the current backend foundation. Other event names are reserved and should be handled defensively until their feature-specific payloads are finalized.

## Listen To Notifications

Register listeners before calling `startRealtime()` when possible.

```ts
connection.on("ReceiveNotification", (payload: NotificationPayload) => {
  // 1. Add item to notification list.
  // 2. Increment unread count.
  // 3. Show in-app toast if desired.
  // No refetch is required for this item.
});
```

Example reducer update:

```ts
function onReceiveNotification(payload: NotificationPayload) {
  notificationsStore.add({
    id: payload.notificationId,
    type: payload.notificationType,
    title: payload.title,
    content: payload.content,
    imageUrl: payload.imageUrl,
    createdAt: payload.createdAt,
    reference: payload.referenceId
      ? {
          id: payload.referenceId,
          type: payload.referenceType,
        }
      : null,
  });
}
```

## Join And Leave Groups

Groups are generic. For chat, use a stable group name convention.

Recommended conversation group:

```text
conversation:{conversationId}
```

Join when opening a conversation:

```ts
await connection.invoke("JoinGroup", `conversation:${conversationId}`);
```

Leave when closing it:

```ts
await connection.invoke("LeaveGroup", `conversation:${conversationId}`);
```

## Listen To Messages

Message realtime events are reserved by the backend foundation. Wire handlers now, but keep payload parsing defensive until message payloads are finalized.

```ts
connection.on("ReceiveMessage", payload => {
  // Append to active conversation if it matches.
  // Update conversation list preview.
  // Increment unread count if the conversation is not active.
});

connection.on("MessageEdited", payload => {
  // Update message content in local cache.
});

connection.on("MessageDeleted", payload => {
  // Mark message deleted or remove it from local cache.
});

connection.on("MessagesRead", payload => {
  // Update read receipts.
});
```

## Typing

Typing events are reserved. Use conversation groups.

```ts
export async function sendTypingStarted(conversationId: string) {
  await connection?.invoke("JoinGroup", `conversation:${conversationId}`);
  // Backend-specific typing send method is not exposed yet.
}

connection.on("TypingStarted", payload => {
  // Show typing indicator.
});

connection.on("TypingStopped", payload => {
  // Hide typing indicator.
});
```

## Presence

Presence events are reserved:

```ts
connection.on("UserOnline", payload => {
  // Mark user online.
});

connection.on("UserOffline", payload => {
  // Mark user offline.
});
```

## Push Notification Data

FCM data payload values are strings.

Notification push data:

```json
{
  "notificationId": "uuid",
  "notificationType": "1",
  "referenceId": "uuid",
  "referenceType": "0"
}
```

Use this data for deep-link navigation when the user taps the push.

Example:

```ts
function handleNotificationPress(data: Record<string, string>) {
  const referenceType = Number(data.referenceType);
  const referenceId = data.referenceId;

  if (!referenceId) return;

  switch (referenceType) {
    case 0:
      navigation.navigate("UserProfile", { userId: referenceId });
      break;
    case 1:
      navigation.navigate("PostDetail", { postId: referenceId });
      break;
    case 3:
      navigation.navigate("Conversation", { conversationId: referenceId });
      break;
  }
}
```

Message push notifications do not create rows in the `Notifications` table. They are only for the mobile notification tray and deep-linking into a conversation.

## Recommended App Lifecycle

On app launch:

1. Load saved access token.
2. If authenticated, create SignalR connection.
3. Register realtime listeners.
4. Start SignalR.
5. Request notification permission.
6. Get FCM token.
7. Register device token.

On token refresh:

1. Update stored access token.
2. Restart SignalR connection if needed.
3. Keep same listeners.

On logout:

1. Unregister FCM token if available.
2. Stop SignalR.
3. Clear local notification/message cache if desired.
4. Clear auth tokens.

## Error Handling

For REST device-token APIs:

- `401`: access token is missing/expired.
- `400`: invalid request body, usually missing token or invalid platform.
- `200`: operation accepted.

For SignalR:

- If connection fails with auth error, refresh token and reconnect.
- If network drops, `withAutomaticReconnect` retries automatically.
- Keep UI resilient; realtime may lag, REST remains source of truth for full reloads.

## Troubleshooting

Connection never starts:

- Check `{API_URL}/hubs/realtime` is correct.
- Check `accessTokenFactory` returns a non-empty access token.
- Check token contains `token_type = access`.
- Check backend CORS if connecting from web.

No notification event received:

- Confirm the user is connected after login.
- Confirm backend emitted `ReceiveNotification`.
- Confirm listener is registered before or immediately after connection start.
- Confirm the logged-in user id matches the notification recipient.

Push not received:

- Confirm device permission is granted.
- Confirm FCM token is registered with `POST /api/device-token/register`.
- Confirm token is active for the current user.
- Current backend has push sender interface ready; Firebase adapter must be enabled server-side before real FCM delivery.

Duplicate events:

- Ensure the app creates one shared SignalR connection per logged-in session.
- Remove old listeners before re-registering if a screen mounts repeatedly:

```ts
connection.off("ReceiveNotification");
connection.on("ReceiveNotification", handler);
```

## Backend Status

Implemented now:

- `/hubs/realtime`
- JWT auth for SignalR
- User/group sends through backend `IRealtimeService`
- Device token register/unregister APIs
- `ReceiveNotification` payload contract
- Push abstraction

Not fully enabled yet:

- Real Firebase Admin SDK sender
- Final payloads for message, typing, presence, conversation events
- Server methods for client-originated typing/presence events
