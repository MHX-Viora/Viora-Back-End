using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Viora.Application.Advertisements;

namespace Viora.Infrastructure.Advertisements;

public sealed class AdvertisementLifecycleHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<AdvertisementLifecycleHostedService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(1));
        do
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IAdvertisementService>()
                    .SynchronizeLifecycleAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Advertisement lifecycle synchronization failed.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
