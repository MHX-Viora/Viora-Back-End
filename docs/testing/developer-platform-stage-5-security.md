# Stage 5 security verification

Executed 2026-10-17. Paths in the evidence column are classes under
`Viora.Application.Tests` unless prefixed SDK (`../viora/packages/miniapp-sdk`).
The 27 rows map the threat categories retained in the approved specification.
Passing checks establish the stated boundary, not protection against arbitrary XSS
or a compromised partner backend.

| # | Threat | Executed evidence / boundary |
| --- | --- | --- |
| 1 | Authorization code reuse | `SsoTokenEndpointTests.RealExchangeProducesSignedBoundIdentityAndOpaqueUserInfoToken`; PostgreSQL concurrent redemption has one winner. |
| 2 | PKCE bypass/mismatch | `SsoAuthorizationTests.LibraryValidationRejectsUnsafeProtocolRequests`; `SsoTokenEndpointTests.RedemptionRejectsWrongBinding`. S256 required for both types. |
| 3 | Client mismatch | `SsoSecurityRegressionTests.AValidDifferentClientCannotRedeemAnotherClientsCode`: a registered eligible second app cannot redeem the first app's code. |
| 4 | Redirect mismatch | `SsoTokenEndpointTests.RedemptionRejectsWrongBinding`; exact callback tests in `SsoClientPolicyTests`. |
| 5 | Expired code/token | `SsoSecurityRegressionTests.ExpiredCodeAndAccessTokenAreRejectedWithoutTimingSleeps`: real server TimeProvider advances ten minutes; SDK signed ID-token expiry test. |
| 6 | State injection | SDK `tests/auth.test.mjs`: state/callback binding rejected before exchange; confidential callback injection in `examples/protocol.e2e.mjs`. |
| 7 | Nonce mismatch | SDK signed ID-token nonce rejection; authorization requires nonce; confidential uses expectedNonce. |
| 8 | Issuer substitution | SDK signed issuer rejection and discovery endpoint-origin substitution test. |
| 9 | Audience mismatch | SDK signed ID-token audience rejection; real exchange validates intended client audience. |
| 10 | Forged signature | SDK signed fixture with wrong key rejected; real server JWKS validates actual ID token, including overlap configuration. |
| 11 | Scope escalation | `SsoAuthorizationTests`, `SsoClientPolicyTests`; `SsoConsentServiceTests.OnlyVerifiedEmailAndGrantedProfileClaimsReachThePrincipal`. |
| 12 | Consent bypass | `SsoConsentEndpointTests`: missing CSRF/changed ticket, nonce, scope and deny; real first/repeat/forced consent in Chrome. |
| 13 | Access-token replay | Bearer tokens remain replayable while valid. Real UserInfo succeeds before revocation and fails immediately afterward; no DPoP/mTLS claim. |
| 14 | Refresh replay | `SsoRefreshPolicyTests`: issuance and offline_access disabled for both types; rotation/family reuse is deliberately unavailable. |
| 15 | Cross-client token use | `SsoTokenManagementTests`: only allowlisted confidential backend introspects its own token; separate pairwise identities in both real examples. |
| 16 | Suspended app | `SsoTokenEndpointTests.SuspensionAfterCodeIssuanceBlocksExchange` and UserInfo lifecycle matrix. |
| 17 | Missing/expired domain proof | `SsoClientPolicyTests` plus `SsoSecurityRegressionTests.EnvironmentAndDomainChangesBetweenCodeAndExchangeFailClosed`. |
| 18 | CSRF/login CSRF | `SsoBootstrapTests`, `SsoConsentEndpointTests`; partner logout rejects forged CSRF in real HTTPS E2E; BFF tests reject cross-origin/no-CSRF requests. |
| 19 | XSS-sensitive rendering | `SsoConsentPageTests.BrowserReceivesAccessibleEncodedConsentFormWithSecurityHeaders`; partner renders textContent/HTML-escaped identity and restrictive CSP. Arbitrary same-origin script compromise is outside this guarantee. |
| 20 | Open redirect | `SsoAuthorizationTests.InvalidClientAndRedirectAreNeverUsedAsAnErrorDestination`; `SsoBootstrapTests.OnlyLocalAuthorizationReturnsAreAccepted`. |
| 21 | Account-link takeover | `SsoAccountLinkTests`, endpoint tests and isolated PostgreSQL proof replay/race: owner/current credential/consent/explicit approval; stable tombstones, no email merge. |
| 22 | UserInfo authorization | `SsoSecurityRegressionTests.UserInfoRejectsMissingSocialAndUrlTransportCredentials`, lifecycle tests, SDK subject mismatch; social JWT never authorizes this resource. |
| 23 | Bridge spoofing | SDK `tests/bridge.test.mjs`: exact source/origin/ID/schema/version/permission/timeout, pending limit and cleanup; controlled Chrome iframe handshake/context/close evidence at checkpoint 22. |
| 24 | Credential leakage | SDK config rejects secret, bridge rejects credential context; account-link redaction PG test; BFF excludes confidential credential routes. Example logs print origins only, protocol test failures print stage only. |
| 25 | Session fixation | `SsoSessionTests`, bootstrap browser binding and single consumption; SDK logout-in-flight test; `tests/partner-session.test.mjs` opaque one-use/expiry/cancellation/duplicate-cookie rejection. |
| 26 | Cross-environment use | `SsoClientPolicyTests`, registry/migration/consent persistence tests; actual code exchange rejects client changed to Production. |
| 27 | Credential rotation/stale authentication | `MiniAppSsoPostgresTests` current-secret recovery/rotation, registry tests; `SsoRegistrationTests` bounded distinct overlap keys/fail-closed loading; real JWKS overlap exchange and old-code redemption after test-only new-key promotion. |

## Controlled Chrome protocol run

Three distinct HTTPS origins: issuer `127.0.0.1:5490`, Public SPA
`localhost:5491`, Confidential backend `127.0.0.2:5492`. Temporary CA, private keys,
accounts, registry and SQLite storage belong only to the fixture. Seeded domain
proof and first-party user login replace external provisioning only; actual
OpenIddict authorization, consent, exchange, JWKS, UserInfo and revocation execute.
Local certificate warning exceptions were accepted without changing system trust.

- Public: first login/consent, signed exchange/UserInfo, reload clears identity,
  repeat login skips consent and preserves subject, local logout, login again,
  forced consent, owner revoke in another tab, UserInfo fails and clears identity,
  authorization asks consent again; deny returns a safe unauthenticated error.
- Confidential: independent first consent and subject, server exchange, reload
  keeps protected partner session, UserInfo succeeds, logout clears partner only,
  repeat login skips consent, owner revoke makes UserInfo fail and clears partner
  session, renewed consent restores the same pairwise subject.
- Successful public/confidential final pages: no console errors/warnings.
  Expected certificate errors during setup, Public failed discovery before the
  issuer exception, and deliberate denied/revoked requests are not clean-run
  successes. No token/secret/cookie inspection or full network-body capture.

`npm run test:sso:e2e` repeats both protocol/session lifecycles over real HTTPS
with the actual SDK/backend, including partner CSRF and callback injection. It is
an HTTP driver; cookie SameSite and CSP are covered by the separate Chrome run.
Setup and exact commands: [operations](../operations/developer-platform-stage-5.md).

Public Internet domains, reverse-proxy deployment, native WebViews, multi-replica
rollout, external social login providers and production key rotation were not
executed. Disposable PostgreSQL tests separately cover migrations/transactions/
concurrency; the browser fixture does not substitute for those tests.
