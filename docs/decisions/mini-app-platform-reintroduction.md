# ADR: Reintroduce Mini Apps inside Utilities

Status: accepted, 2026-10-12.

Context: the user authorized the entire platform after retirement, requiring discovery in existing Utilities immediately below Wallet. Existing ANKT authentication, backend architecture and Admin review remain the integration points.

Decision: reuse Developer/version/consent and maintained OpenIddict; expose bounded published catalog projections instead of mutable drafts. Use the existing PostgreSQL connection for both contexts with separate protocol migration history. Add migrations after retirement, preserving applied history. Catalog hide is editorial; suspension blocks access/tokens. Favorites are account-owned; PostgreSQL advisory transaction locks serialize writes. Recent discovery uses 90 days; popularity counts distinct authenticated successful users over seven days, so repeat opens cannot increase rank.

Alternatives rejected: a separate discovery site/tab conflicts with placement; a second ANKT DB duplicates identity/operations; custom code exchange duplicates maintained OIDC. Legacy public exchange returns 410; internal test adapters remain for historical coverage.

Consequences: native needs a client rebuild/device verification. Sandboxed iframe cannot guarantee third-party cookies; web offers an external website link and independent OIDC login. Partner BFF keeps secrets/tokens. Production requires reviewed history, separate issuer/API hostnames, certificates and HTTPS/proxy configuration. No production changes performed.
