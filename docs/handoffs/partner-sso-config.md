# Partner SSO configuration

Node example and integrations/nghe-thuat-so-ankt now accept ANKT_CLIENT_ID, ANKT_CLIENT_SECRET and ANKT_REDIRECT_URI. Default public endpoints: https://api.mxh.ankt.vn and https://app.ankt.vn. Website origin and callback route derive from the redirect URI; legacy variables and explicit staging overrides remain supported.

HTTPS callbacks default to production when NODE_ENV is unset. Callback URLs reject credentials, query/fragment and reserved application paths. Secrets remain backend-only; state/PKCE/session controls are unchanged. Production requires deployed hybrid BE/SDK and the actual ANKT embedding origin to match the configured host.

Validation: focused configuration tests plus existing Node identity/session tests. No deployment or real credential use. The actual Nghệ Thuật Số repository is not in this workspace; PROMPT.md carries the contract for its integration.
