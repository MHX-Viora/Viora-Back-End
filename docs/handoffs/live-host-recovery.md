# Live host recovery

The host screen reads `GET /api/lives/mine/active` on entry. If a Live remains active after an unexpected exit, it presents the session and lets the host explicitly end it. While broadcasting, the client sends `POST /api/lives/{id}/heartbeat` every 15 seconds.

`HostLastSeenAt` is nullable. The migration gives sessions already active during deployment a fresh grace period. `LiveHostTimeoutService` sweeps on startup and every 30 seconds. It ends Live/Reconnecting sessions with no heartbeat for 120 seconds, and cancels Preparing sessions after 10 minutes. Null legacy timestamps use `StartedAt` or `CreatedAt` as the fallback. Creating a new Live also finalizes stale sessions before checking for an active one. A fresh session on another device remains protected by its heartbeat.

Manual and automatic endings share `LiveSessionFinalizer`, which serializes on the Live row, closes viewer sessions, flushes comment counts, and sends `LiveEnded`. The migration `AddLiveHostHeartbeat` must be applied before deploying the API. `LiveLifecycle` options can override the timeout and sweep intervals.

Verification: `LiveHostLeaseTests`, backend build, and frontend TypeScript check. Browser verification requires an authenticated account with Live access.
