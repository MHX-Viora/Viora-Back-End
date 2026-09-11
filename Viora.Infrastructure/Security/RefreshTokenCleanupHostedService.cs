using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Accounts;

namespace Viora.Infrastructure.Security;

public sealed class RefreshTokenCleanupHostedService(
    IServiceScopeFactory scopeFactory,
    IOptions<JwtOptions> options,
    ILogger<RefreshTokenCleanupHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromHours(24);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(CleanupInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopeFactory.CreateAsyncScope();
                var repository = scope.ServiceProvider.GetRequiredService<IAccountRepository>();
                var cutoff = DateTime.UtcNow.AddDays(-options.Value.RefreshTokenRetentionDays);
                var deleted = await repository.DeleteRetainedRefreshTokensAsync(cutoff, stoppingToken);
                logger.LogInformation("Refresh token cleanup deleted {DeletedCount} retained rows", deleted);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Refresh token cleanup failed");
            }
        }
    }
}
