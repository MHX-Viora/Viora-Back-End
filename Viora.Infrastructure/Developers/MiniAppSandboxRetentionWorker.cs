using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Viora.Infrastructure.Developers;

public sealed class MiniAppSandboxRetentionWorker(IServiceScopeFactory scopes,MiniAppSandboxOptions options,TimeProvider clock,ILogger<MiniAppSandboxRetentionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if(!options.Enabled)return;
        // Delay the first sweep so host startup/schema preparation is not a cleanup trigger.
        using var timer=new PeriodicTimer(TimeSpan.FromMinutes(1),clock);
        try
        {
            while(await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope=scopes.CreateScope();
                    await scope.ServiceProvider.GetRequiredService<MiniAppSandboxRetentionService>().SweepAsync(100,stoppingToken);
                }
                catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){break;}
                catch(Exception)
                {
                    // Do not log row content, SQL parameters, credentials or raw provider errors.
                    logger.LogWarning("Sandbox retention sweep unavailable; deferred until the next scheduled sweep.");
                }
            }
        }
        catch(OperationCanceledException) when(stoppingToken.IsCancellationRequested){ }
    }
}
