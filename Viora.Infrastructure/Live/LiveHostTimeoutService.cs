using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Live;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.LiveStreaming;

public sealed class LiveHostTimeoutService(IServiceScopeFactory scopes, IOptions<LiveLifecycleOptions> options,
    ILogger<LiveHostTimeoutService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SweepAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
            catch (Exception exception) { logger.LogError(exception, "Failed to expire abandoned Live sessions"); }
            await Task.Delay(TimeSpan.FromSeconds(Math.Max(5, options.Value.SweepIntervalSeconds)), stoppingToken);
        }
    }

    private async Task SweepAsync(CancellationToken cancellationToken)
    {
        using var scope = scopes.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var now = DateTime.UtcNow;
        var liveCutoff = now.AddSeconds(-options.Value.HostTimeoutSeconds);
        var preparingCutoff = now.AddSeconds(-options.Value.PreparingTimeoutSeconds);
        var candidates = await db.Lives.AsNoTracking()
            .Where(x => (x.Status == LiveStatus.Preparing && (x.HostLastSeenAt ?? x.CreatedAt) < preparingCutoff) ||
                        ((x.Status == LiveStatus.Live || x.Status == LiveStatus.Reconnecting) &&
                         (x.HostLastSeenAt ?? x.StartedAt ?? x.CreatedAt) < liveCutoff))
            .OrderBy(x => x.CreatedAt).Select(x => x.Id).Take(100).ToListAsync(cancellationToken);
        var finalizer = scope.ServiceProvider.GetRequiredService<LiveSessionFinalizer>();
        foreach (var id in candidates)
        {
            try
            {
                var result = await finalizer.EndAsync(id, true, cancellationToken);
                if (result?.Changed == true) logger.LogInformation("Expired abandoned Live {LiveId}", id);
            }
            catch (Exception exception) { logger.LogError(exception, "Failed to expire Live {LiveId}", id); }
        }
    }
}
