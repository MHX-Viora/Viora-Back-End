using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Live;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.LiveStreaming;

public sealed class LiveCommentCountFlusher(
    ILiveCommentBuffer buffer,
    IServiceScopeFactory scopes,
    IOptions<LiveChatOptions> options,
    ILogger<LiveCommentCountFlusher> logger) : BackgroundService
{
    private readonly ConcurrentDictionary<Guid, SemaphoreSlim> gates = new();

    public async Task FlushAsync(Guid liveId, CancellationToken cancellationToken = default)
    {
        var gate = gates.GetOrAdd(liveId, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var count = buffer.TakePendingCount(liveId);
            if (count == 0) return;
            try
            {
                using var scope = scopes.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var updated = await db.Lives.Where(x => x.Id == liveId).ExecuteUpdateAsync(
                    x => x.SetProperty(live => live.TotalComments, live => live.TotalComments + count), cancellationToken);
                if (updated != 1) throw new InvalidOperationException($"Live {liveId} was not found while flushing comment count.");
            }
            catch
            {
                buffer.RestorePendingCount(liveId, count);
                throw;
            }
        }
        finally { gate.Release(); }
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(1, options.Value.StatsFlushSeconds)));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            foreach (var liveId in buffer.PendingLiveIds())
            {
                try { await FlushAsync(liveId, stoppingToken); }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
                catch (Exception error) { logger.LogError(error, "Live comment count flush failed for {LiveId}", liveId); }
            }
            buffer.PruneClosed(DateTimeOffset.UtcNow);
        }
    }

    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        await base.StopAsync(cancellationToken);
        foreach (var liveId in buffer.PendingLiveIds())
        {
            try { await FlushAsync(liveId, cancellationToken); }
            catch (Exception error) { logger.LogError(error, "Final live comment count flush failed for {LiveId}", liveId); }
        }
    }
}
