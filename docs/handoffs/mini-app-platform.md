# Mini App Platform v1 handoff

- Data: nine Mini App tables, seven seeded permission codes, PostgreSQL indexes, migration `AddMiniAppPlatform`.
- Public: dynamic list/detail, consent/revoke, launch, one-time exchange.
- Management: admin apps/developers and developer-owned registration/review/rotation; partner configuration uses header credentials.
- Mobile: `/mini-apps` center and `/mini-apps/[id]` secured shared WebView; legacy AntiFake runtime removed.
- Security: HTTPS/domain boundaries, password-hashed secrets, SHA-256 launch-code lookup, conditional atomic consume, pairwise subject, scoped profile projection, rate limiting, redacted audit data.
- SDK/docs: hosted `mini-app-sdk/ankt-mini-app.js` and `docs/MINI_APP_INTEGRATION.md`.
