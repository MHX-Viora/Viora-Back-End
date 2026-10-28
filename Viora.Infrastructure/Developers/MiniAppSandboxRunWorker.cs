using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxRunWorker(IServiceScopeFactory scopes,MiniAppSandboxOptions options,TimeProvider clock,ILogger<MiniAppSandboxRunWorker> logger):BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        using var timer=new PeriodicTimer(TimeSpan.FromSeconds(1),clock);
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope=scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<MiniAppSandboxRunCoordinator>().ExecuteNextAsync(stoppingToken);
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception){logger.LogWarning("Sandbox run processing unavailable; reconciliation deferred until the next scheduled tick.");}
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){ }
    }
}
