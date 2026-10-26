# Sandbox accounts in the existing identity schema

> Separate-account/consent policy superseded on 2026-10-26 by
> [shared automatic login](sandbox-shared-auto-login.md). The record below is historical.

Date: 2026-10-24. Status: accepted for local implementation; rollout remains pending.

Use Accounts/Users for both the shared presentation template and per-Developer /
Mini App test accounts, with an explicit IsSandbox marker. Reuse the existing
pairwise subject, consent, OpenIddict client and code/token implementation.
Keep missing non-KYC template profile fields in SandboxIdentityTemplates; do not
create fake verification records or duplicate the production profile subsystem.

A shared production Account ID or shared browser cookie/token would join partner
data across developers. Separate mapped accounts preserve the existing unique
email index and pairwise mapping while exposing one sample profile. Production
queries hide these rows and JWT issuance denies them. Never remove the marker
while such rows remain; the migration enforces that on downgrade.

The existing issuer can authenticate a temporary, client-bound Sandbox cookie.
A separate 60-second POST ticket bootstraps that cookie from a live owner session.
No new hostname, impersonation via query userId, social refresh token, or direct
access token issuance is needed. A token-free issuer document navigates to the
partner after POST because Chromium also applies form-action to HTTP redirects.
Consent remains the existing owner-approved protocol interaction.

Legacy generic OIDC tests intentionally construct policy-neutral consent/
authorization components. Sandbox boundary tests and browser E2E use the full
application registrations; disabling the feature never permits real-user fallback
in the deployed application.
