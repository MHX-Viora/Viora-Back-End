using Microsoft.Extensions.Configuration;
using Viora.Application.Calls;

namespace Viora.Infrastructure.Realtime;

public sealed class IceServerProvider(IConfiguration configuration) : IIceServerProvider
{
    public IceServersResponse Get()
    {
        var servers = new List<IceServerResponse>
        {
            new(["stun:stun.l.google.com:19302"])
        };
        var turnUrl = configuration["Calls:Turn:Url"];
        if (!string.IsNullOrWhiteSpace(turnUrl))
        {
            servers.Add(new([turnUrl], configuration["Calls:Turn:Username"], configuration["Calls:Turn:Credential"]));
        }
        return new IceServersResponse(servers);
    }
}
