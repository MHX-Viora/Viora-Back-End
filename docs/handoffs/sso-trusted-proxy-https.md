# SSO HTTPS behind a trusted reverse proxy

## Problem and scope
User reached OpenIddict discovery over external HTTPS but received `invalid_request` / `ID2083` because Render terminates TLS and the application had no forwarded-header middleware. This is independent of certificate provisioning. Retain OpenIddict HTTPS enforcement, credentials, OAuth/PKCE and existing host/IP policies.

## Implementation
- `viora-BE/Hosting/TrustedProxyHeaders.cs`: bounded configurable IP/CIDR allowlist, scheme-only `X-Forwarded-Proto`, one hop. Loopback only by default. Reject invalid IPs and /0 networks. Never clear trust lists without repopulating explicit trusted entries. No trust-all cloud switch.
- `viora-BE/Program.cs`: register settings and run forwarding first, before Sandbox/Production host boundaries, redirection and authentication.
- Single diagnostic per process on discovery when forwarded scheme remains HTTP. Logs only immediate peer IP and configuration names, not headers/query/tokens. Operator must verify ownership of the ingress and configure `ReverseProxy__KnownProxies__0` or a confirmed `KnownNetworks` CIDR.
- `.env.example`: documented commented configuration, no guessed Render IP/range.
- Tests exercise trusted/untrusted/mapped peers, one-hop header selection, stable host/IP, absent trust and invalid/unbounded configuration. Protocol fixture now uses the production forwarding middleware; discovery tests verify real OpenIddict success for trusted headers and continued HTTPS rejection otherwise.

## Verification
Initial tests failed because the hosting helper did not exist. First test-server run had one Windows Event Log permission failure; cleared default logging providers in the isolated fixture (no production change). Then 31 tests passed including registration and actual code/token exchange. Final expanded run and build results are recorded below after completion.

- Final regression: **189 passed, 0 failed**, covering all SSO-filtered tests and trusted-proxy tests, including consent/code/token exchange over the simulated HTTP proxy hop.
- Expanded focused run: **35 passed** before adding the full proxy-hop code exchange assertion; the final 189 run includes that assertion.
- Release backend build: **0 warnings, 0 errors**. Relevant Git whitespace checks passed. Security review confirmed no arbitrary proxy trust, no host/IP rewriting, no disabling of HTTPS, no secret logging, and no certificate/credential changes.

## Deployment
Committed production code and proxy example comments as `f7bd4634` (`fix(sso): honor HTTPS from trusted proxies`), following the existing SSO commit authorization. Docs/logs/tests/secrets were excluded. Not pushed or deployed. Deploy the code, call discovery, then if still HTTP inspect `Immediate peer` in Render logs, verify that peer, and add its IP to the trusted list. Restart and verify discovery, consent, callback and token exchange. Render's actual peer/subnet cannot be inferred from outbound IPs or this screenshot. No remote trust configuration or credentials were changed.

## Primary references
- https://learn.microsoft.com/en-us/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-8.0
- https://learn.microsoft.com/en-us/aspnet/core/breaking-changes/8/forwarded-headers-unknown-proxies
- https://community.render.com/t/setting-network-port-forward-reverse-proxy-manually-fusionauth/8419
