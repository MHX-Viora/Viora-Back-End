# Developer credentials workflow

DeveloperMiniAppsController no longer consumes ChangeDomain reauth for draft
updates or domain challenge creation. Authentication and management-service
account/ownership/capability checks remain required. Domain verification, technical
tests, immutable submitted/published snapshots and administrator approval remain
unchanged. Rotate-secret still consumes a RotateSecret proof before mutation.

Create already generates a secret and stores only its hash. Host defaults to
backend credentials and displays the initial create response once. No API/schema
change is required. Passwordless ANKT accounts can create/update/verify apps;
reauth now directs them to existing verified password recovery for rotation.
Developer registration itself creates no separate credentials.

HTTP regression covers passwordless create, secret absent from detail, domain
configuration update, challenge creation, denied foreign-account writes and
rotation denied without proof. Existing proof binding/expiry/replay tests remain.
Full tests: 192 passed, 6 skipped (environment prerequisites). OAuth signing tests
need execution outside sandbox restrictions. No migration, DB write or deploy.
