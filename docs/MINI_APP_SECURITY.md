# Mini App security model

## Trust and authorization

Registration and admin approval do not establish permanent trust in a website. Validate every request. Backend role checks protect admin routes; Developer ownership/membership checks protect partner resources. Current app/Developer/account status gates launch, runtime validation and exchange. Emergency suspension is available to admins.

Independent login does not share ANKT passwords, cookies or bearer tokens. An SSO response authenticates a scoped pairwise identity; the partner creates and authorizes its own session. Account linking requires proof of both identities, explicit intent and conflicts handled transactionally.

## URL/domain boundaries

Use public HTTPS DNS hostnames and standard TLS port. Reject userinfo, unsafe schemes, localhost, private literals, link-local/metadata destinations and unapproved hosts. Register callbacks exactly; fragments are not allowed. Native navigation and bridge origins are constrained separately. Web runtimes must not embed their own ANKT parent origin with same-origin privileges.

Domain verification is the only remote content fetch needed by the platform. DNS validation rejects any non-public answer. The actual socket is pinned to a validated address, TLS still verifies the requested hostname, proxies/cookies/decompression/redirects are disabled, and time/header/body limits apply. A second uncontrolled DNS lookup must not reintroduce rebinding.

No server proxy circumvents website iframe policies. CSP frame-ancestors and X-Frame-Options are enforced by the browser. Cross-origin iframe restrictions mean the host cannot reliably inspect a blocked page; the UI provides an external-tab alternative.

## Authorization exchange

- Client secrets are password-hashed and disclosed only at creation/rotation.
- Authorization codes have cryptographic entropy, hashed storage, 60-second expiry and conditional atomic consumption.
- Codes bind user, app/client, exact callback, state hash, S256 challenge and runtime session.
- Expired/closed/suspended sessions, invalid client, wrong redirect/state/verifier, reuse and revoked login consent fail.
- Public clients cannot rely on a secret and must prove the verifier; confidential clients prove both secret and verifier.
- Profile projection uses currently approved and consented scopes; no ANKT account ID or bearer token is returned.
- Logs record action/error codes, never secret values, raw codes or callback URLs containing codes.

## Bridge

Host-only runtime tokens are hashed server-side and kept private in ANKT memory. Page messages bind app/session and a fresh navigation nonce. Web messages additionally require the exact child `Window` source and current approved origin. Native bridge setup is restricted to the trusted top document, with scoped native WebView hardening. Responses are delivered only to the currently active document/context.

Only six methods are supported. Payload size, IDs, types, method schemas, expiry, duplicate IDs, per-session rate and approved/current permission grants are checked. The SDK times out pending calls. The host validates the backend runtime session before processing messages and periodically rechecks status. Navigating, closing or expiring invalidates the old bridge context.

Native dispatch verifies main-frame status and the actual source origin before forwarding messages. Android devices without a verifiable WebMessageListener transport fall back to the browser. Queued JavaScript delivery also checks the current document origin and binding before exposing a response.

Page nonce is a correlation/binding mechanism, not a secret proving website integrity. A compromised approved site can invoke its own approved bridge surface; narrow permissions and explicit ANKT-owned consent remain necessary.

## Sensitive capabilities

This implementation does not automatically grant camera/microphone or arbitrary native downloads. Native permission behavior must be verified on both platforms after a development rebuild; iOS WebView media flags alone do not control Android's native permission callbacks. A scoped Android configuration plugin hardens Mini App WebViews without altering ANKT's livestream/call permissions. Provider OAuth flows that prohibit embedding use the system browser or provider-supported integration.

The current scope catalogue is limited to the seven existing identity/profile/app metadata codes; arbitrary camera/media/file or per-session capability scopes cannot be registered or granted. Current supported grants persist until revocation. Adding sensitive native APIs requires a separate consent/lifecycle design and device validation; catalogue approval alone cannot enable hardware access.

## Operational limits

Browser/device and real PostgreSQL checks are reported separately. Test/build success does not verify partner session revocation, third-party OAuth return flows, DNS ownership on deployed domains, production migration state or website security. See [deployment checklist](MINI_APP_DEPLOYMENT.md).
