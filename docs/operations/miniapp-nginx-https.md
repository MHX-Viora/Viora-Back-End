# Mini App config rejected behind Nginx

Observed 2026-10-28: the ANKT public catalog returns 200, but
`GET /api/miniapp/auth/config?clientId=...` returns
`400 {error:{code:"INVALID_REQUEST"}}`. The same public config on Render returns 200.
For the exact `/config` path this error indicates `Request.IsHttps` is false.
An initial transient 502 disappeared on the subsequent check.

Nginx terminates TLS and the application receives HTTP. It needs both the correct
forwarded scheme and an explicitly trusted immediate proxy peer. Verify the TLS
virtual host for `api.mxh.ankt.vn` and the actual running Viora systemd unit.
Do not infer the unit name from an unrelated project or restart a guessed service.

In the existing HTTPS proxy location, Nginx should overwrite the scheme header:

```nginx
proxy_set_header X-Forwarded-Proto $scheme;
```

Keep the existing upstream/port and other proxy settings. Validate with `sudo nginx -t`
before `sudo systemctl reload nginx`. If another proxy terminates TLS before Nginx,
review that trusted hop rather than blindly forwarding a client-supplied header.

After deploying the diagnostic change, one warning per process identifies the peer
and whether the forwarded header exists, including failures at `/config`:

```bash
sudo journalctl -u <confirmed-viora-service> -n 150 --no-pager | grep -E 'ANKT.ReverseProxy|Immediate peer|Forwarded proto'
```

- Missing header: fix the effective Nginx location/header forwarding.
- Header present but peer untrusted: confirm that IP is the ingress, then configure
  `ReverseProxy__KnownProxies__0=<confirmed-peer-IP>` in the running service environment.
- Trusted peer but HTTP remains: check Nginx `$scheme`/upstream TLS topology.

Loopback proxies are trusted by default. Do not allow all proxies, spoof HTTPS in
application code, or disable the Auth Bridge HTTPS check. No access token, secret,
cookie or query value is included in the diagnostic log.

The fix requires access to the live proxy/service configuration; current SSH access
was unavailable. Backend tests cover configured peers, untrusted headers, missing
headers, config/exchange diagnostics and rejection of unbounded trust settings.
