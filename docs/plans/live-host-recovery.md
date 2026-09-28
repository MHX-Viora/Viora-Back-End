# Live host recovery plan

1. Add host heartbeat timestamp and configurable expiration, with a focused boundary test.
2. Extract the existing end operation so manual end and timeout use one cleanup path.
3. Add heartbeat, active-session lookup, and a periodic stale-session sweep.
4. Show active-session recovery on the host setup screen and send heartbeats while broadcasting.
5. Verify backend build/tests, frontend type check, and the active-session interaction.
