# Cloudinary Profile Images Handoff

## Configuration

Cloudinary credentials are required at startup. Store them with User Secrets locally:

```powershell
dotnet user-secrets set "Cloudinary:CloudName" "YOUR_CLOUD_NAME" --project viora-BE
dotnet user-secrets set "Cloudinary:ApiKey" "YOUR_API_KEY" --project viora-BE
dotnet user-secrets set "Cloudinary:ApiSecret" "YOUR_API_SECRET" --project viora-BE
```

Production environment variable equivalents:

```text
Cloudinary__CloudName
Cloudinary__ApiKey
Cloudinary__ApiSecret
```

Never commit or log these values.

## Endpoints

- `POST /api/users/profile` creates a profile with optional `avatar` and `cover` files.
- `PATCH /api/users/profile` partially updates profile fields and either image.
- Bearer access token required.
- Content type: `multipart/form-data`; image fields: `avatar` and `cover`.
- Accepted: JPEG, PNG, WebP; maximum 5 MB.
- Success returns the updated profile, including the Cloudinary HTTPS URL now stored in PostgreSQL.

Example:

```bash
curl -X PATCH "https://localhost:7001/api/users/profile" \
  -H "Authorization: Bearer ACCESS_TOKEN" \
  -F "displayName=New name" \
  -F "avatar=@avatar.webp"
```

## Operational Notes

- No database migration is required; existing `AvatarUrl` and `CoverUrl` columns are used.
- Assets use `viora/users/{accountId}/profile/avatar|cover`; replacement overwrites the previous asset.
- Provider failures return HTTP 502 without exposing Cloudinary error details or credentials.
- SDK: CloudinaryDotNet 1.29.2.

Official references: [Cloudinary .NET upload](https://cloudinary.com/documentation/dotnet_image_and_video_upload), [Cloudinary .NET configuration](https://cloudinary.com/documentation/dotnet_integration), [NuGet package](https://www.nuget.org/packages/CloudinaryDotNet/1.29.2).
