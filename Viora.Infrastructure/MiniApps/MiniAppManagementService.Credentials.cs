using Microsoft.EntityFrameworkCore;
using Viora.Application.MiniApps;

namespace Viora.Infrastructure.MiniApps;

public sealed partial class MiniAppManagementService
{
    public async Task<MiniAppCredentialMetadata> GetCredentialsAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        var canRotate = true;
        try { await RequireAppCapability(accountId, app, "credentials", ct); }
        catch (MiniAppException error) when (error.StatusCode == 403) { canRotate = false; }
        return new(app.ClientId, app.ClientAuthenticationMethod.ToString(), !string.IsNullOrEmpty(app.ClientSecretHash),
            app.PreviousClientSecretExpiresAt > DateTime.UtcNow ? app.PreviousClientSecretExpiresAt : null, app.SecretRotatedAt, canRotate);
    }

    public async Task<MiniAppCredentialResponse> RotateSecretAsync(Guid accountId, Guid id, int graceMinutes, CancellationToken ct)
    {
        if (graceMinutes is < 0 or > 1440) throw new MiniAppException("VALIDATION_ERROR", "Thời gian chuyển tiếp phải từ 0 đến 1440 phút.", 422);
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "credentials", ct);
        var now = DateTime.UtcNow;
        if (await db.MiniAppAuditLogs.CountAsync(log => log.ActorAccountId == accountId && log.Action == "SecretRotated" && log.CreatedAt >= now.AddMinutes(-10), ct) >= 5)
            throw new MiniAppException("RATE_LIMITED", "Chỉ được cấp secret tối đa 5 lần trong 10 phút.", 429);
        if (app.PreviousClientSecretExpiresAt > now)
            throw new MiniAppException("SECRET_TRANSITION_ACTIVE", "Thu hồi secret cũ hoặc chờ hết thời gian chuyển tiếp trước khi cấp tiếp.", 409);
        var secret = credentials.CreateSecret();
        app.PreviousClientSecretHash = graceMinutes > 0 ? app.ClientSecretHash : null;
        app.PreviousClientSecretExpiresAt = graceMinutes > 0 ? now.AddMinutes(graceMinutes) : null;
        app.ClientSecretHash = credentials.HashSecret(secret);
        app.SecretRotatedAt = now;
        Audit(accountId, app.DeveloperId, id, "SecretRotated", $"GraceMinutes={graceMinutes}");
        await SaveAsync(ct);
        return new(app.Id, app.ClientId, secret);
    }

    public async Task RevokePreviousSecretAsync(Guid accountId, Guid id, CancellationToken ct)
    {
        var app = await RequireOwned(accountId, id, ct);
        await RequireAppCapability(accountId, app, "credentials", ct);
        app.PreviousClientSecretHash = null; app.PreviousClientSecretExpiresAt = null;
        Audit(accountId, app.DeveloperId, id, "PreviousSecretRevoked");
        await SaveAsync(ct);
    }
}
