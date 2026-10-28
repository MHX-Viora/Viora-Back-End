using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppProductionWorker(IServiceScopeFactory scopes, TimeProvider clock, ILogger<MiniAppProductionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1), clock);
        try
        {
            do
            {
                try
                {
                    await using var scope = scopes.CreateAsyncScope();
                    var result = await scope.ServiceProvider.GetRequiredService<MiniAppProductionRetentionService>().CleanupAsync(stoppingToken);
                    if (result.EventsDeleted + result.GrantsExpired + result.SessionsClosed + result.GrantsDeleted + result.SessionsDeleted > 0)
                        logger.LogInformation("Mini App cleanup: events={Events}, expiredGrants={ExpiredGrants}, closedSessions={ClosedSessions}, grants={Grants}, sessions={Sessions}",
                            result.EventsDeleted, result.GrantsExpired, result.SessionsClosed, result.GrantsDeleted, result.SessionsDeleted);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { break; }
                catch (Exception) { logger.LogError("Mini App runtime cleanup failed; retry on the next scheduled tick."); }
            } while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { }
    }
}
