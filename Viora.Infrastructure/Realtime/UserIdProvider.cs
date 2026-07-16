using Microsoft.AspNetCore.SignalR;

namespace Viora.Infrastructure.Realtime;

public sealed class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
    {
        var value = connection.User?.FindFirst("user_id")?.Value;
        return Guid.TryParse(value, out var userId) ? userId.ToString() : null;
    }
}
