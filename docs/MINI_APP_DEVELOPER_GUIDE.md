# Developer registration and publishing

Open /developers in ANKT, or Personal profile → Settings → Dành cho nhà phát triển. /developers/register and /developers/console use your existing ANKT account. Detailed setup: [SETUP.md](mini-app-partner/SETUP.md).

1. Save Individual/Business profile, verify contact email, accept current terms and submit review. Admin approval enables app management; membership role controls capabilities.
2. Create Mini App draft: name/slug/category, HTTPS entry, domains/origins, media, support/privacy/terms and approved capabilities. Independent keeps your website login; AnktSso enables maintained OIDC. Optional/Required and Auto Login do not silently grant permission.
3. Verify each domain using the Console HTTPS proof file. Register callback exactly, including path/case/trailing slash. Run technical checks, submit immutable version and wait for Admin review.
4. Publish the approved version. Draft edits do not change the live snapshot. Mini App appears below Wallet in Utilities when available; Admin controls catalog visibility/banners/featured and may suspend app/developer.
5. Client secret is shown only on confidential client creation/rotation; store it on your backend. Public clients use None + mandatory PKCE. Obtain issuer/host metadata from your ANKT operator; never guess deployment readiness or place tokens/secrets in frontend variables.
6. Integrate Node/ASP.NET BFF and React/SDK examples linked in setup. Test denied/revoked consent, callback replay, account switch, blocked cookies and app suspension before production. User can revoke consent through Ứng dụng đã kết nối.

Console includes application versions/domains, technical tests, publication, credentials, team and real usage analytics. No developer account login on Admin. Custom legacy exchange is retired; use OIDC discovery/token/JWKS and your backend's /auth/ankt/* routes.
