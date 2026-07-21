using Microsoft.EntityFrameworkCore;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class DeviceTokenRepository(AppDbContext dbContext) : IDeviceTokenRepository
{
    public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens.SingleOrDefaultAsync(deviceToken => deviceToken.Token == token, cancellationToken);

    public Task<DeviceToken?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens.SingleOrDefaultAsync(deviceToken => deviceToken.DeviceId == deviceId, cancellationToken);

    public async Task<DeviceToken?> GetByTokenOrDeviceIdAsync(
        string token,
        string? deviceId,
        CancellationToken cancellationToken)
    {
        var byToken = await GetByTokenAsync(token, cancellationToken);
        if (byToken is not null || deviceId is null)
        {
            return byToken;
        }

        return await GetByDeviceIdAsync(deviceId, cancellationToken);
    }

    public async Task<IReadOnlyList<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.DeviceTokens
            .Where(deviceToken =>
                deviceToken.UserId == userId &&
                deviceToken.IsActive)
            .ToListAsync(cancellationToken);

    public async Task<IReadOnlyList<DeviceToken>> GetActiveByTokenSuffixAsync(string tokenSuffix, CancellationToken cancellationToken) =>
        await dbContext.DeviceTokens
            .Where(deviceToken =>
                deviceToken.IsActive &&
                deviceToken.Token.EndsWith(tokenSuffix))
            .ToListAsync(cancellationToken);

    public Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens.AddAsync(deviceToken, cancellationToken).AsTask();

    public Task DeactivateAsync(string token, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens
            .Where(deviceToken => deviceToken.Token == token)
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(deviceToken => deviceToken.IsActive, false)
                    .SetProperty(deviceToken => deviceToken.LastSeenAt, DateTime.UtcNow),
                cancellationToken);

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
