# Live host recovery

An interrupted host must not leave a Live permanently blocking another session. The host can see and explicitly end an active session after reopening the host screen. A heartbeat records host presence while broadcasting; the server ends Live and Preparing sessions that exceed their grace periods. End remains idempotent and performs the same viewer, chat, and broadcast cleanup for manual and automatic endings. Active sessions on another device must never be closed merely because the host opens the setup screen.
