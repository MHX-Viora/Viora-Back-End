# Admin image upload authorization — 2026-10-28

- Bug: `POST /api/admin/images` required the optional `user_id` profile claim. Admin accounts can legitimately have no `User` profile; `JwtTokenService` emits `sub`, `role`, `sid` but only emits `user_id` when a profile exists. Upload returned 401 despite a valid admin token.
- Fix: derive the upload owner from the verified `sub` account claim. Folder remains `viora/admin/{id}/images`, now consistently keyed by admin account ID. Existing stored images/URLs remain valid. No migration/config/frontend/token changes.
- Existing bearer authentication, signature/lifetime checks and `[Authorize(Roles = "2")]` remain enforced. Missing account identity is still rejected. Profile IDs are not used as account IDs.
- Regression reproduced before the fix: real signed JWT from `JwtTokenService` for admin without a profile reached the endpoint but received 401. HTTP test uses actual JWT bearer validation and multipart input with mock storage, asserting anonymous/invalid-token 401, non-admin 403, admin-without-profile 200 and account ownership. Existing unit test also supplies different account/profile IDs to ensure `sub` wins.
- Verify: `dotnet test Viora.Application.Tests --no-restore --filter 'FullyQualifiedName~AdminImageUploadTests|FullyQualifiedName~FeaturedBrand' -v quiet -m:1 -nr:false /p:UseSharedCompilation=false`.
- Production needs deployment of this BE change. No real account tokens were inspected; no production uploads, commits or deployments performed by this task.
