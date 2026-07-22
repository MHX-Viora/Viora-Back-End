# Account Change Password

## Summary

- Added `PUT /api/account/change-password`.
- Requires JWT auth and reads the account id from the access token `sub` claim.
- Request body: `currentPassword`, `newPassword`, `confirmPassword`.
- Success response: `{ "message": "Đổi mật khẩu thành công. Vui lòng đăng nhập lại." }`.

## Behavior

- Validates required fields, password length 8-100, uppercase, lowercase, number, confirmation match, and new password different from current password.
- Returns 404 when the account is not found or soft-deleted.
- Returns 400 for invalid current password, same password, or validation failures.
- Updates `PasswordHash` and `UpdatedAt`.
- Revokes all active refresh tokens for the account in the same repository transaction.
- Does not modify the current access token; it expires naturally.

## Notes

- Password hashing is registered through `AspNetIdentityPasswordHasher`, which wraps ASP.NET Core Identity `PasswordHasher<object>`.
- Swagger response metadata is declared for 200, 400, 401, and 404.
