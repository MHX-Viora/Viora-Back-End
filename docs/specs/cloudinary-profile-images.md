# Spec: Cloudinary Profile Images

## Objective
Upload authenticated users' avatar and cover images to Cloudinary and persist the returned HTTPS delivery URL in the existing `Users` row.

## API Contract
- `POST /api/users/profile` and `PATCH /api/users/profile` both accept profile fields and optional image files.
- Bearer access token required.
- `multipart/form-data` fields are `displayName`, `gender`, optional `avatar`, and optional `cover`.
- Accept JPEG, PNG, or WebP up to 5 MB; validate both declared content type and file signature.
- Success returns the updated `UserResponse`; upload/validation failures use Vietnamese messages without provider secrets.

## Architecture and Security
- API validates the untrusted multipart boundary and file header.
- Application verifies the active account/profile, uploads supplied images through an image-storage abstraction, validates the returned URLs, then persists the profile once.
- Infrastructure owns the Cloudinary SDK and signed server-side upload.
- Folders/public IDs are deterministic (`viora/users/{accountId}/profile/avatar|cover`) and uploads overwrite/invalidate the previous asset.
- Only `secure_url` is persisted; Cloudinary credentials come from `Cloudinary__CloudName`, `Cloudinary__ApiKey`, and `Cloudinary__ApiSecret`.

## Dependencies and Commands
- CloudinaryDotNet 1.29.2.
- Test: `dotnet test Viora.Infrastructure.Tests/Viora.Infrastructure.Tests.csproj --no-restore`.
- Build: `dotnet build viora-BE.sln --no-restore`.

## Boundaries
- Always: HTTPS URLs, authenticated ownership, input validation, cancellation support.
- Ask first: public unsigned uploads, videos, or database schema changes.
- Never: commit/log API secrets or trust filename extensions alone.

## Success Criteria
- Valid avatar/cover reaches storage and its returned URL is saved to the matching profile column.
- Invalid, oversized, or non-image files are rejected before Cloudinary.
- Missing profile/inactive account retains existing business errors.
- Tests and build pass.
