# Stage 5 integration and operations

Stage 5 implementation only. Production enablement requires a separate release.
Canonical contracts: [specification](../specs/developer-platform-stage-5.md).
Security evidence: [27-case matrix](../testing/developer-platform-stage-5-security.md).

## Run the examples locally

Requires .NET 8, Node 22.13+, installed SDK dependencies (`npm ci` from
`viora/packages/miniapp-sdk`) and free loopback ports 5490–5492. No real account,
domain, application connection string or production certificate is used.

Terminal 1, backend root:

```powershell
dotnet build tools/SsoE2EHost/SsoE2EHost.csproj --no-restore -m:1 -p:UseSharedCompilation=false
dotnet run --project tools/SsoE2EHost --no-build --no-restore --no-launch-profile
```

The host prints a generated temporary directory path, never a secret. In Terminal
2, SDK root, set that path locally and start both examples:

```powershell
$fixtureDirectory = 'PASTE_GENERATED_FIXTURE_DIRECTORY'
npm run build
npm run example:fixture -- $fixtureDirectory
```

Open issuer `https://127.0.0.1:5490/`, public `https://localhost:5491/` and
confidential `https://127.0.0.2:5492/`. Accept only these disposable local certificate
warnings. Do not install their CA into system trust. Public Login enters real
authorization; the fixture offers a clearly marked local user continuation before
the actual consent page. The issuer home has CSRF-protected owner revocation.

Terminal 3, SDK root, same generated directory:

```powershell
$fixtureDirectory = 'PASTE_GENERATED_FIXTURE_DIRECTORY'
$env:NODE_EXTRA_CA_CERTS = Join-Path $fixtureDirectory 'root-ca.pem'
npm run test:sso:e2e
Remove-Item Env:NODE_EXTRA_CA_CERTS
```

Run once per fixture or allow its normal SSO rate-limit window to reset before
rapid repeated runs. Never disable TLS validation. The runner reads the private
fixture file directly and supplies the confidential secret only to the backend
child. Do not print/upload that file, TLS key, certificates or protocol responses.
Stop partner processes first, then Ctrl+C the issuer: it disposes its database and
deletes only its checked temporary directory. Abrupt termination can leave the
directory; resolve its absolute path inside the OS temporary root and verify the
`ankt-oidc-browser-` prefix before removing it. Never target application storage.

Windows sandbox may deny certificate private-key operations or esbuild child
processes. Use the approved test execution context; do not change application
crypto or globally relax certificate validation to bypass it.

## Integrate a partner

Use Developer Portal → Mini App → Đăng nhập ANKT. Select Sandbox or Production
explicitly, Public or Confidential, openid plus needed scopes and exact HTTPS
callbacks. Domain verification and eligible lifecycle remain mandatory. Production
configuration does not publish an app. Always save current app/client versions;
409 means reload and reconcile. Metadata is available only when runtime enabled.

Public: import `@ankt/miniapp-sdk` from this private workspace, use initialize →
login → handleRedirect → getUser. Remove callback query before rendering/loading
other resources. Tokens stay in memory; reload reacquires with top-level login.
Local logout clears the SDK only. Use `(configured issuer, validated sub)` as the
partner identity, never email. No refresh issuance or native API promise.

Confidential: provision the current registry credential directly to backend secret
storage via owner backend tooling; never via frontend config/BFF/build variables.
`examples/confidential/server.mjs` uses standard openid-client with signature,
issuer, audience, nonce, state, S256 and UserInfo-sub validation. Browser receives
only a Secure HttpOnly host-only SameSite=Lax opaque session handle. The sample
uses bounded in-memory storage: restarts lose sessions, multiple replicas require
a shared transactional store and shared session protection. Validate the current
resource before protected partner actions; revocation is discovered on UserInfo,
not through a promise of remote browser-cookie deletion.

Both example servers intentionally bind loopback. Before adapting them to public
hosting, supply trusted fixed HTTPS origins, secure secret provisioning, distributed
storage, process supervision, resource limits and request/response log redaction.

## Runtime configuration and environments

SSO is disabled by default. Explicit enablement uses `Sso__Enabled=true`,
`Sso__Issuer` (stable external HTTPS issuer), `Sso__FirstPartyOrigin` (exact HTTPS
origin), `Sso__SigningCertificatePath` / `SigningCertificatePassword`, and
`Sso__EncryptionCertificatePath` / `EncryptionCertificatePassword`. Provision
distinct RSA private keys of at least 2048 bits with appropriate purpose and
validity; keep them separate from HTTPS, social JWT and registry encryption keys.
Code lifetime is at most 60 seconds and access lifetime at most 300 seconds.
Passwords belong in secret storage, not source or command history.

Introspection additionally requires exact `Sso__IntrospectionClientIds__0` etc.;
only eligible confidential backends can inspect their own tokens. Preserve the
existing social Bearer, DeveloperSession and Admin permission boundaries. Persist
and share ASP.NET Data Protection keys with access controls in real multi-instance
hosting; the local fixture deliberately uses ephemeral keys.

Sandbox/Production bind client/redirect/scope/consent/resource independently.
Production also requires Published status. Do not copy Sandbox consent, callback,
secret or fixture keys into Production. Deploy approved additive migrations through
the normal database release process; this implementation never applies them to
the configured application database. Review rollback data loss before any downgrade.

## Signing and encryption key rotation

Optional arrays `Sso:PreviousSigningCertificates` and
`Sso:PreviousEncryptionCertificates` contain `{Path, Password}` entries, at most
three per purpose. Environment form: `Sso__PreviousSigningCertificates__0__Path`
and `Sso__PreviousSigningCertificates__0__Password` (equivalent encryption names).
All entries require valid private RSA keys. Duplicate/shared keys across purposes
fail startup with redacted validation errors. Restart is required after changes.

1. Provision a distinct valid replacement with a later NotAfter than the old key.
   OpenIddict chooses a valid certificate by expiry, not by the property name.
2. Register old and new keys on every node during a controlled overlap. JWKS must
   expose both signing public keys and no private material. For a rolling deployment,
   pre-stage a shorter-lived replacement on all nodes, then reissue its certificate
   with a later expiry to promote it only after all nodes know that same public key.
   A coordinated maintenance restart is an alternative. Do not mix unaware nodes.
3. Retain old signing keys beyond the maximum ID-token lifetime plus clock skew
   and verifier JWKS cache horizon. ID-token lifetime is library-configured; inspect
   actual exp/iat privately during release validation. Do not assume it equals the
   five-minute access-token lifetime. Retain old encryption keys until every code
   and persisted access-token payload they protect has expired, plus clock skew.
4. Verify new issuance, old-token verification/decryption, discovery/JWKS and
   controlled public/confidential exchanges before retiring old entries. Retain
   secure rollback material until the change is verified; rotate before expiry.

Local tests cover multiple-key startup, redacted failure, public JWKS, actual
exchange with overlap and old-key code redemption after test-only new-key
promotion. Live fleet rollout/cache propagation is a release check.
For key compromise, revoke affected grants/tokens and invalidate relevant sessions;
do not retain a compromised key merely to preserve uninterrupted login.

Registry client-secret rotation is separate: authenticated owner backend calls the
versioned rotation API; previous credential becomes invalid immediately. Coordinate
partner secret replacement. Unlink, local logout and consent revocation have
different effects; see the canonical handoff endpoints and SDK README.

## Troubleshooting and release gate

Issuer mismatch: align external scheme/host and trusted proxy forwarding. CORS:
the Public origin must match an eligible client's verified callback; do not widen
social CORS. invalid_grant: restart login after used/expired code, wrong callback,
PKCE, lifecycle or environment change. 409: reconcile versions; no blind retries.
429: respect rate limits. UserInfo denial: clear partner identity and reauthorize;
never fall back to stale claims. SDK failure messages intentionally omit protocol
details. Redact callback query, request bodies, Authorization and Set-Cookie at the
proxy/APM layer as well as in application logs.

Before a separately authorized release: public-domain and reverse-proxy E2E,
approved PostgreSQL migration application, protected key provisioning/rotation,
monitoring and rollback, shared session storage, privacy/consent UX and operational
ownership. Stage 6, native integration and npm publication require separate scope.

References: [OpenIddict multiple credentials and selection](https://documentation.openiddict.com/configuration/encryption-and-signing-credentials),
[openid-client code exchange](https://github.com/panva/openid-client/blob/main/docs/functions/authorizationCodeGrant.md).
