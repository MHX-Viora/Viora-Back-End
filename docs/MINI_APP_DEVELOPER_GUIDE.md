# Developer registration and publishing

## Access

In ANKT open **Personal profile → Settings and activity → Developer Portal**. This opens `/developer` inside ANKT using the current signed-in account, without a second login or admin access. Submit a Developer profile with name, contact email and optional company/phone/public HTTPS website. Admin approval activates the profile before app creation.

The in-app interface provides owned app list/create/edit, authentication mode/permissions, exact domain proof, version submission/status/history, SSO integration/client credentials and usage/audit. The separately deployed Vite web application also retains its developer-only portal with team management; its Developer login does not grant admin rights.

An administrator may instead create a Developer from the Developers page and link the owner's ANKT AccountId. Team invitations use an existing account ID; only the owner can add/remove members. An account belongs to one Developer. Team membership grants access to that Developer's apps, not administrative approval rights.

## Create wizard

1. Identity: name, slug, icon/logo URL, description and category.
2. Website and domains: public HTTPS website; register exact domain names. SSO additionally needs exact callback URLs and bridge origins.
3. Authentication: `Independent` keeps your existing login. `AnktSso` enables the explicit identity exchange.
4. Permissions: request only necessary available scopes. `identity.login` is required for ANKT identity. Profile email/phone require separate approved grants.
5. Preview and create a draft. Complete domain verification before submitting review.

Store a displayed client secret in your server's secret store immediately. It is returned once, never placed in frontend code or URLs. Rotation invalidates the previous secret. Public clients use authentication method `None` and must use S256 PKCE; confidential clients additionally authenticate with their secret.

## Domain ownership

In the Domains screen prepare verification for every registered exact host, then download the prefilled `ankt-mini-app-verification.txt` file. Upload it unchanged to the website root, without creating a subdirectory. It must be available at:

```text
https://your-domain.example/ankt-mini-app-verification.txt
```

The endpoint must return HTTP 200 directly over valid HTTPS without redirection. It must resolve entirely to public IP addresses. The verifier limits body size, elapsed time and repeat attempts. A verification token proves host ownership; it is not a login credential. Avoid wildcard domains for new registrations because every host must be explicitly verified.

## Review and updates

Submit the draft for review. The reviewer checks identity, domains, callback/origin boundaries and each requested permission. Rejection includes a reason; edit and resubmit. Published app configuration changes create a new reviewable version. Only approval publishes the submitted snapshot; unpublished edits never expand user access.

Suspended developers/apps cannot launch. Emergency suspension also invalidates runtime validation. Archived apps disappear from discovery. Usage analytics show recent launches/failures and unique users; audit/version history records administrative and developer actions.

See [SSO integration](MINI_APP_SSO.md), [SDK](MINI_APP_SDK.md), [admin workflow](MINI_APP_ADMIN_WORKFLOW.md).
