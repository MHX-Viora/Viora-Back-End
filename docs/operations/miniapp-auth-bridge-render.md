# Render: Mini App Auth Bridge

Use the existing backend and API domain for Sandbox and Production. The public
Bridge client ID is the registered App ID in both environments. Environment is
selected from the authenticated host actor, never from a Mini App request flag.

Import [the example](../examples/miniapp-auth-bridge.render.env.example) after
reviewing the target deployment. Required Mini App settings:

```dotenv
MiniAppAuth__Enabled=true
MiniAppAuth__AllowedHostOrigin=https://mxh.ankt.vn
SandboxTest__Enabled=true
```

One exact host origin covers ANKT Web and Developer Portal on this origin.
HTTPS is required. Keep database, primary ANKT JWT, DeveloperAuth, Gmail, BFF
and existing MiniApps encryption keys. No OIDC issuer, certificate, callback,
frontend client secret or separate Sandbox backend is required.

## Deployment checks

1. Back up the verified database. Review/apply the additive Bridge and shared
   Sandbox account migrations through the normal deployment process.
2. Deploy matching backend, Portal and SDK; set frontend build flag
   `EXPO_PUBLIC_MINI_APP_AUTH_BRIDGE_ENABLED=true` and existing API URL.
3. Verify developer owner Sandbox launch and approved Production launch, UserInfo,
   source logout, wrong-origin rejection and primary JWT API token isolation.
4. Apply the reversible archive migration only after reviewing
   [the legacy store inventory](miniapp-legacy-sso-cleanup.md).

## Removable settings after successful deployment

The replacement no longer reads these legacy settings. Remove them manually
from Render only after the matching revision is healthy:

```text
Sso__Enabled
Sso__Issuer
Sso__FirstPartyOrigin
Sso__CertificateFormat
Sso__SigningCertificatePath
Sso__SigningCertificatePassword
Sso__EncryptionCertificatePath
Sso__EncryptionCertificatePassword
Sso__DevelopmentCertificateDirectory
Sso__AuthorizationCodeSeconds
Sso__AccessTokenSeconds
Sso__IntrospectionClientIds__<index>
Sso__PreviousSigningCertificates__<index>__Path
Sso__PreviousSigningCertificates__<index>__Password
Sso__PreviousSigningCertificates__<index>__Format
Sso__PreviousEncryptionCertificates__<index>__Path
Sso__PreviousEncryptionCertificates__<index>__Password
Sso__PreviousEncryptionCertificates__<index>__Format
```

`<index>` denotes each configured array index (for example `0`); remove those
keys only when present. The list follows the retired SsoOptions properties.

Also replace `MiniAppAuth__HostOrigin` with `MiniAppAuth__AllowedHostOrigin` and
remove the retired `SandboxTest__Issuer`, `SandboxTest__PortalOrigin`,
`SandboxTest__AdditionalPortalOrigins__*`, and `SandboxTest__SessionMinutes` if
present. The Bridge launch lifetime is controlled by the implementation.

The OIDC Secret Files `ankt-sso-signing.pfx.b64` and
`ankt-sso-encryption.pfx.b64` are no longer read by the replacement. Keep a
protected backup for deployment rollback; removing deployed secrets/certificates
requires separate approval. This change does not modify Render state.
