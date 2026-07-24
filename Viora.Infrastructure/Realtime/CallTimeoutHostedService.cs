using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Viora.Application.Calls;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;

namespace Viora.Infrastructure.Realtime;

public sealed class CallTimeoutHostedService(
    IServiceScopeFactory scopeFactory,
    ILogger<CallTimeoutHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);
        while (!stoppingToken.IsCancellationRequested && await timer.WaitForNextTickAsync(stoppingToken))
        {
            await MarkTimedOutCallsAsync(stoppingToken);
        }
    }

    private async Task MarkTimedOutCallsAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();
            var cutoff = DateTime.UtcNow - CallTimeout;
            var callIds = await dbContext.CallSessions
                .AsNoTracking()
                .Where(call => call.Status == CallStatus.Calling && call.StartedAt <= cutoff)
                .OrderBy(call => call.StartedAt)
                .Select(call => call.Id)
                .Take(50)
                .ToListAsync(cancellationToken);

            foreach (var callId in callIds)
            {
                await mediator.Send(new MarkMissedCallCommand(callId), cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Failed to mark timed out calls.");
        }
    }
}
