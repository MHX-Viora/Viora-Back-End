# ANKT Sandbox on Render

This document configures the optional managed iframe/executor host. For the
shared test account and Auth Bridge flow, use
[Sandbox test identity](sandbox-test-identity.md); that flow needs no new domain.

The existing backend serves `/sandbox-host/*` and `/sandbox-runtime/*`.
Add a hostname to this service; no separate service or Mini App is required.
Target configuration: portal `mxh.ankt.vn`, host `sandbox.ankt.vn`, existing
backend `viora-back-end-xmco.onrender.com`. Use the existing backend API origin.

## 1. Register the hostname

In Render, select the existing backend service, then **Settings → Custom Domains
→ Add Custom Domain**. Enter `sandbox.ankt.vn`.

In the DNS provider managing `ankt.vn`, add this record:

| Type | Name | Target | TTL |
| --- | --- | --- | --- |
| CNAME | sandbox | viora-back-end-xmco.onrender.com | Auto/default |

Some providers require the full name `sandbox.ankt.vn`. The target has no
`https://` prefix or path. Do not replace the root or `mxh` records. If `sandbox`
already has records, inspect their purpose before changing them. Avoid conflicting
A/AAAA records on this hostname.

Return to Render, click **Verify**, and wait for the domain and TLS certificate
to become valid. DNS alone does not register a custom domain with Render.

References: [Render custom domains](https://render.com/docs/custom-domains),
[subdomain DNS](https://render.com/docs/configure-other-dns).

## 2. Configure the existing backend

Confirm Sandbox database migrations listed in
[Stage 6 operations](developer-platform-stage-6.md#before-enabling) have been
applied to the intended database. This setup does not run migrations.

Set `MiniAppSandbox__Issuer` to the existing backend API origin in
[the managed executor example](../examples/sandbox.render.env.example).
Keep existing non-retired application secrets. This optional managed executor
is separate from the direct iframe Auth Bridge login path.

The backend must recognize HTTPS after Render's TLS termination. If it sees HTTP,
configure its confirmed ingress proxy using the existing
`ReverseProxy__KnownProxies__0` or bounded `ReverseProxy__KnownNetworks__0`
settings. Keep forwarding trust restricted to confirmed ingress peers.

## 3. Configure the frontend build host

Set these in the environment running the FE build/proxy (Vercel if used), not
only on the backend Render service:

```dotenv
DEVELOPER_SITE_ORIGIN=https://mxh.ankt.vn
ANKT_API_BASE_URL=https://viora-back-end-xmco.onrender.com
EXPO_PUBLIC_OPEN_API_ISSUER=https://viora-back-end-xmco.onrender.com/
ANKT_SANDBOX_HOST_ORIGIN=https://sandbox.ankt.vn
```

Use the actual deployed SSO issuer if different. Preserve the existing
`ANKT_BFF_SHARED_SECRET`. Rebuild/redeploy the frontend so its Sandbox CSP permits
the host. A localhost Developer portal differs from the production PortalOrigin;
verify the deployed portal at `https://mxh.ankt.vn/developers` for this setup.

## 4. Verify

Open `https://sandbox.ankt.vn/sandbox-host/config`: expect HTTP 200 and
`{"portalOrigin":"https://mxh.ankt.vn"}`. Then sign into the deployed Developer
portal and open an owned Mini App's Sandbox: its `/sandbox/config` request should
return 200 with `hostOrigin` equal to `https://sandbox.ankt.vn`. Start a session
and verify the host/bridge connection.

`SANDBOX_DISABLED` means the serving backend process still has the feature off.
A host config 404 can indicate disabled Sandbox, the wrong HostOrigin/service,
or HTTPS forwarding failure. A working config does not prove app eligibility,
database migrations, or SSO readiness; use the session request's actual error.

## Disable

Set `MiniAppSandbox__Enabled=false` on the backend and restart. Keep DNS, existing
keys and database history. Do not undo database migrations as a feature rollback.
