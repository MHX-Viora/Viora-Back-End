# ANKT Mini App integration

Current integration: [partner setup](mini-app-partner/SETUP.md), [platform operation](mini-app-partner/PLATFORM-SETUP.md), [current handoff](handoffs/mini-app-platform.md).

Mini Apps are inside existing Utilities, directly below Wallet. Independent apps use their own login; AnktSso apps use maintained OpenIddict Authorization Code + S256 PKCE and explicit user consent. Partner BFF keeps state/nonce/verifier, client secret, access/ID token and its session. Identity mapping uses verified issuer + pairwise sub; never automatically merge by email.

Developer Console: /developers/register and /developers/console in existing ANKT frontend/auth. Register/verify/submit profile; create app draft; verify HTTPS domains and exact callbacks/origins; run technical tests; submit snapshot; Admin reviews; Developer publishes approved version. Public catalog and runtime configuration use the published snapshot.

Protocol/resource/SDK routes live on the separate public issuer hostname, within the same BE and database. Internal social/wallet/Admin APIs stay on the internal hostname; partner tokens cannot authenticate them. Legacy POST /api/mini-app-auth/exchange and custom authorize return HTTP 410.

Use [Node OIDC](../examples/mini-app-oidc/README.md), [ASP.NET OIDC](../examples/mini-app-oidc-dotnet/README.md), [React](../examples/mini-app-react/README.md) and [typed SDK package](../packages/ankt-mini-app-sdk/README.md). New SDK names preserve SDK 2.2 capabilities. Web iframe has external website fallback for blocked CSP/cookies; native needs a rebuilt client and device verification.

Database migrations are additive after applied retirement, not a second database or data recovery. Local tests/builds passed; production rollout requires configured SMTP, HTTPS, issuer/proxy and persistent keys plus approved migration/deployment.
