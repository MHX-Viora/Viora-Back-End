using Microsoft.AspNetCore.SignalR;

namespace Viora.Infrastructure.Realtime;

public sealed class UserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection) =>
        connection.User?.FindFirst("user_id")?.Value;
}
