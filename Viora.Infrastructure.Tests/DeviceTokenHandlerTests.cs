using Viora.Application.Realtime;
using Viora.Domain.Entities;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class DeviceTokenHandlerTests
{
    [Fact]
    public async Task Register_creates_active_device_token_when_token_is_new()
    {
        var userId = Guid.NewGuid();
        var repository = new FakeDeviceTokenRepository();
        var handler = new RegisterDeviceTokenHandler(repository, new RegisterDeviceTokenValidator());

        var response = await handler.Handle(new RegisterDeviceTokenCommand(
            userId,
            "token-1",
            "device-1",
            "Samsung S24",
            DevicePlatform.Android,
            "1.0.0"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.True(response.IsActive);
        var token = Assert.Single(repository.DeviceTokens);
        Assert.Equal(userId, token.UserId);
        Assert.Equal("token-1", token.Token);
        Assert.True(token.IsActive);
        Assert.NotNull(token.LastSeenAt);
        Assert.Equal(1, repository.SaveCount);
    }

    [Fact]
    public async Task Register_moves_existing_token_to_current_user_without_duplicate()
    {
        var previousUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var existing = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = previousUserId,
            Token = "token-1",
            IsActive = false,
            Platform = DevicePlatform.Ios
        };
        var repository = new FakeDeviceTokenRepository(existing);
        var handler = new RegisterDeviceTokenHandler(repository, new RegisterDeviceTokenValidator());

        var response = await handler.Handle(new RegisterDeviceTokenCommand(
            currentUserId,
            "token-1",
            null,
            null,
            DevicePlatform.Android,
            null), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(repository.DeviceTokens);
        Assert.Equal(currentUserId, existing.UserId);
        Assert.Equal(DevicePlatform.Android, existing.Platform);
        Assert.True(existing.IsActive);
        Assert.NotNull(existing.LastSeenAt);
    }

    [Fact]
    public async Task Register_updates_existing_device_id_with_new_token_and_user()
    {
        var previousUserId = Guid.NewGuid();
        var currentUserId = Guid.NewGuid();
        var existing = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = previousUserId,
            Token = "old-token",
            DeviceId = "device-1",
            IsActive = false,
            Platform = DevicePlatform.Android
        };
        var repository = new FakeDeviceTokenRepository(existing);
        var handler = new RegisterDeviceTokenHandler(repository, new RegisterDeviceTokenValidator());

        var response = await handler.Handle(new RegisterDeviceTokenCommand(
            currentUserId,
            "new-token",
            "device-1",
            "Samsung S24",
            DevicePlatform.Android,
            "1.0.0"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Single(repository.DeviceTokens);
        Assert.Equal(currentUserId, existing.UserId);
        Assert.Equal("new-token", existing.Token);
        Assert.Equal("device-1", existing.DeviceId);
        Assert.True(existing.IsActive);
    }

    [Fact]
    public async Task Register_clears_old_device_row_when_existing_token_uses_same_device_id()
    {
        var currentUserId = Guid.NewGuid();
        var tokenRow = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = "new-token",
            IsActive = false,
            Platform = DevicePlatform.Android
        };
        var oldDeviceRow = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = Guid.NewGuid(),
            Token = "old-token",
            DeviceId = "device-1",
            IsActive = true,
            Platform = DevicePlatform.Android
        };
        var repository = new FakeDeviceTokenRepository(tokenRow, oldDeviceRow);
        var handler = new RegisterDeviceTokenHandler(repository, new RegisterDeviceTokenValidator());

        var response = await handler.Handle(new RegisterDeviceTokenCommand(
            currentUserId,
            "new-token",
            "device-1",
            "Samsung S24",
            DevicePlatform.Android,
            "1.0.0"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.Equal("device-1", tokenRow.DeviceId);
        Assert.Equal(currentUserId, tokenRow.UserId);
        Assert.True(tokenRow.IsActive);
        Assert.Null(oldDeviceRow.DeviceId);
        Assert.False(oldDeviceRow.IsActive);
    }

    [Fact]
    public async Task Unregister_marks_existing_token_inactive()
    {
        var userId = Guid.NewGuid();
        var existing = new DeviceToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Token = "token-1",
            IsActive = true,
            Platform = DevicePlatform.Android
        };
        var repository = new FakeDeviceTokenRepository(existing);
        var handler = new UnregisterDeviceTokenHandler(repository, new UnregisterDeviceTokenValidator());

        var response = await handler.Handle(new UnregisterDeviceTokenCommand(userId, "token-1"), CancellationToken.None);

        Assert.True(response.Success);
        Assert.False(response.IsActive);
        Assert.False(existing.IsActive);
        Assert.Equal(1, repository.SaveCount);
    }

    private sealed class FakeDeviceTokenRepository(params DeviceToken[] tokens) : IDeviceTokenRepository
    {
        public List<DeviceToken> DeviceTokens { get; } = [.. tokens];
        public int SaveCount { get; private set; }

        public Task<DeviceToken?> GetByTokenAsync(string token, CancellationToken cancellationToken) =>
            Task.FromResult(DeviceTokens.SingleOrDefault(deviceToken => deviceToken.Token == token));

        public Task<DeviceToken?> GetByDeviceIdAsync(string deviceId, CancellationToken cancellationToken) =>
            Task.FromResult(DeviceTokens.SingleOrDefault(deviceToken => deviceToken.DeviceId == deviceId));

        public Task<DeviceToken?> GetByTokenOrDeviceIdAsync(string token, string? deviceId, CancellationToken cancellationToken)
        {
            var byToken = DeviceTokens.SingleOrDefault(deviceToken => deviceToken.Token == token);
            return Task.FromResult(byToken ?? (deviceId is null
                ? null
                : DeviceTokens.SingleOrDefault(deviceToken => deviceToken.DeviceId == deviceId)));
        }

        public Task<IReadOnlyList<DeviceToken>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DeviceToken>>(
                DeviceTokens.Where(deviceToken => deviceToken.UserId == userId && deviceToken.IsActive).ToList());

        public Task AddAsync(DeviceToken deviceToken, CancellationToken cancellationToken)
        {
            DeviceTokens.Add(deviceToken);
            return Task.CompletedTask;
        }

        public Task DeactivateAsync(string token, CancellationToken cancellationToken)
        {
            var deviceToken = DeviceTokens.SingleOrDefault(value => value.Token == token);
            if (deviceToken is not null)
            {
                deviceToken.IsActive = false;
                deviceToken.LastSeenAt = DateTime.UtcNow;
            }

            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }
}
