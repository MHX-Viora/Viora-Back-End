using MediatR;
using Viora.Domain.Entities;

namespace Viora.Application.Realtime;

public sealed class RegisterDeviceTokenHandler(
    IDeviceTokenRepository repository,
    FluentValidation.IValidator<RegisterDeviceTokenCommand> validator)
    : IRequestHandler<RegisterDeviceTokenCommand, DeviceTokenResponse>
{
    public async Task<DeviceTokenResponse> Handle(RegisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new DeviceTokenResponse(false, false);
        }

        var now = DateTime.UtcNow;
        var deviceToken = await repository.GetByTokenAsync(request.Token, cancellationToken);
        if (deviceToken is null)
        {
            deviceToken = new DeviceToken
            {
                Id = Guid.NewGuid(),
                Token = request.Token
            };
            await repository.AddAsync(deviceToken, cancellationToken);
        }

        deviceToken.UserId = request.UserId;
        deviceToken.DeviceId = string.IsNullOrWhiteSpace(request.DeviceId) ? null : request.DeviceId.Trim();
        deviceToken.DeviceName = string.IsNullOrWhiteSpace(request.DeviceName) ? null : request.DeviceName.Trim();
        deviceToken.Platform = request.Platform;
        deviceToken.AppVersion = string.IsNullOrWhiteSpace(request.AppVersion) ? null : request.AppVersion.Trim();
        deviceToken.IsActive = true;
        deviceToken.LastSeenAt = now;

        await repository.SaveChangesAsync(cancellationToken);
        return new DeviceTokenResponse(true, true);
    }
}

public sealed class UnregisterDeviceTokenHandler(
    IDeviceTokenRepository repository,
    FluentValidation.IValidator<UnregisterDeviceTokenCommand> validator)
    : IRequestHandler<UnregisterDeviceTokenCommand, DeviceTokenResponse>
{
    public async Task<DeviceTokenResponse> Handle(UnregisterDeviceTokenCommand request, CancellationToken cancellationToken)
    {
        var validation = await validator.ValidateAsync(request, cancellationToken);
        if (!validation.IsValid)
        {
            return new DeviceTokenResponse(false, false);
        }

        var deviceToken = await repository.GetByTokenAsync(request.Token, cancellationToken);
        if (deviceToken is null)
        {
            return new DeviceTokenResponse(true, false);
        }

        if (deviceToken.UserId == request.UserId)
        {
            deviceToken.IsActive = false;
            deviceToken.LastSeenAt = DateTime.UtcNow;
            await repository.SaveChangesAsync(cancellationToken);
        }

        return new DeviceTokenResponse(true, false);
    }
}
