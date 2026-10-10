# Hybrid Mini App Platform architecture

## Components

The platform extends the existing ASP.NET Core 8 / EF Core / PostgreSQL Mini App module. The Expo client owns discovery, the embedded runtime and in-app Developer registration/management using its ordinary ANKT session. The existing Vite admin app hosts administrative review and an additional independently authenticated developer portal. The profile settings entry opens the Expo management interface, not the administrative website.

```mermaid
sequenceDiagram
    participant User
    participant Host as ANKT runtime
    participant ANKT as ANKT API
    participant Site as Partner website
    participant Partner as Partner backend
    User->>Host: Open approved app
    Host->>ANKT: Launch (ANKT authentication)
    ANKT-->>Host: Website URL + private runtime session
    Host->>Site: Load website without ANKT credentials
    Site->>Partner: Start login or explicit account linking
    Partner-->>Site: State + S256 challenge, verifier kept server-side
    Site->>Host: SDK requestLogin (bound bridge)
    Host->>ANKT: Authorize exact callback + state + PKCE
    ANKT-->>Host: Missing consent or one-time callback URL
    Host->>User: Request missing approved permissions
    Host-->>Site: Callback URL after approval
    Site->>Partner: Navigate callback with code/state
    Partner->>ANKT: Exchange code + redirect + state + verifier
    ANKT-->>Partner: Pairwise subject and consented claims
    Partner-->>Site: Partner session; clean redirect without code
```

Independent apps stop after website load and retain their own login. SSO apps explicitly opt into the remainder. This is a dedicated Mini App identity exchange, not a claim that ANKT implements a full OIDC discovery/ID-token server.

## Registry and versions

The persisted legacy `Active` app status means approved and published. Existing enum values and data remain compatible. Draft/PendingReview/Rejected/Suspended/Archived provide the lifecycle; immutable version records keep submitted and reviewed configurations separate from published configuration. A published app continues serving its approved configuration while a new version is under review. Developers cannot self-approve.

Registry contains identity, developer/category, public website, exact navigation/bridge origins, callbacks, auth mode, client authentication method, scopes, published version and timestamps. Category records are administratively managed. New app domains must complete public HTTPS ownership verification before review.

## Identity boundaries

- ANKT owns its account, authentication, grants, runtime session and pairwise subject.
- Partners own their accounts, passwords, account linking, business authorization and session cookies.
- Developer and admin clients have separate authentication state; API authorization is authoritative.
- Mini App pages receive bridge context identifiers and a nonce, never ANKT bearer tokens, client secrets or the private runtime session token.
- Favorite/recent app identifiers are stored on the client scoped to the ANKT account. Registry/catalogue content always comes from the API.

## Runtime

Native uses a shared React Native WebView with isolated cookies, allowlisted HTTPS navigation and an external system-browser option. Web uses a sandboxed cross-origin iframe, exact origin/source checking and an external-tab fallback. ANKT does not proxy sites to defeat CSP or `X-Frame-Options`.

Runtime sessions expire and close; session validation rechecks current account/app/developer status and current grants. Page bridge contexts rotate across navigation. Suspension prevents new launches and makes existing runtime validation fail; a previously established partner session remains under the partner's control.

## Data

Existing Developer/MiniApp/Permission/Consent/LaunchCode/ExternalIdentity/Audit/LaunchLog tables are extended. Additive tables cover memberships, categories, immutable versions, verified domains, runtime sessions and reports. Launch logs supply usage statistics. Codes, client secrets and runtime tokens are hashed; partner state is hashed and PKCE binds redemption.

See [security model](MINI_APP_SECURITY.md), [SSO guide](MINI_APP_SSO.md), [deployment](MINI_APP_DEPLOYMENT.md) and [verification report](MINI_APP_TESTING.md).
