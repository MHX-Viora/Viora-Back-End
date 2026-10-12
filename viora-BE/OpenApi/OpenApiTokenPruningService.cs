using OpenIddict.Abstractions;

namespace viora_BE.OpenApi;

// Keep redeemed/revoked entries for at least one day for replay detection and investigation.
public sealed class OpenApiTokenPruningService(IServiceScopeFactory scopes, ILogger<OpenApiTokenPruningService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromHours(1));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var cutoff = DateTimeOffset.UtcNow.AddDays(-1);
                await scope.ServiceProvider.GetRequiredService<IOpenIddictTokenManager>().PruneAsync(cutoff, stoppingToken);
                await scope.ServiceProvider.GetRequiredService<IOpenIddictAuthorizationManager>().PruneAsync(cutoff, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested) { return; }
            catch { logger.LogError("Open API protocol pruning failed; retry scheduled for the next hour"); }
        }
    }
}
