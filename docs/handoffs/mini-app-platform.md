# Mini App platform handoff

The former v1 handoff incorrectly claimed the mobile runtime was already implemented. The audit recorded in [mini-app-verification.md](mini-app-verification.md) found only a placeholder in the Expo app.

The hybrid implementation now extends the existing backend with a Developer Portal, admin review/version management, marketplace and guarded native/web runtime. Independent partner login and opt-in ANKT SSO are separate modes.

Use [mini-app-hybrid.md](mini-app-hybrid.md) for the current implementation inventory and verification status, [integration](../MINI_APP_INTEGRATION.md) for the contract, and [testing](../MINI_APP_TESTING.md) for commands and unverified environments. Build/test success is not a production deployment or device verification.
