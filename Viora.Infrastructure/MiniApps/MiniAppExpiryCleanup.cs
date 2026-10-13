using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.MiniApps;

public sealed class MiniAppExpiryCleanup(IServiceScopeFactory scopes, ILogger<MiniAppExpiryCleanup> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await CleanupAsync(db, DateTime.UtcNow.AddDays(-1), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogWarning(exception, "Mini App expiry cleanup failed."); }
        }
    }
    public static async Task CleanupAsync(AppDbContext db, DateTime retentionCutoff, CancellationToken ct)
    {
        await db.DeveloperReauths.Where(p => p.ExpiresAt < retentionCutoff).ExecuteDeleteAsync(ct);
        await db.DeveloperEmailVerifications.Where(p => p.ExpiresAt < retentionCutoff && p.VerifiedAt == null).ExecuteDeleteAsync(ct);
        await db.MiniAppRuntimeSessions.Where(s => s.ExpiresAt < retentionCutoff).ExecuteDeleteAsync(ct);
    }
}
