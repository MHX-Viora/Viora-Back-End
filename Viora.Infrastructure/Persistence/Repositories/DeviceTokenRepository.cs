using Microsoft.EntityFrameworkCore;
using Viora.Application.Realtime;
using Viora.Domain.Entities;

namespace Viora.Infrastructure.Persistence.Repositories;

public sealed class DeviceTokenRepository(AppDbContext dbContext) : IDeviceTokenRepository
{
    public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens.SingleOrDefaultAsync(deviceToken => deviceToken.Token == token, cancellationToken);

    public Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken) =>
        dbContext.DeviceTokens.AddAsync(deviceToken, cancellationToken).AsTask();

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
