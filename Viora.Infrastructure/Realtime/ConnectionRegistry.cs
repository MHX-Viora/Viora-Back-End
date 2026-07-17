using System.Collections.Concurrent;
using Viora.Application.Realtime;

namespace Viora.Infrastructure.Realtime;

public interface IConnectionRegistry
{
    void Add(Guid userId, string connectionId);
    void Remove(Guid userId, string connectionId);
    bool IsOnline(Guid userId);
    IReadOnlyCollection<string> GetConnections(Guid userId);
}

public sealed class ConnectionRegistry : IConnectionRegistry, IOnlineUserRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> connections = new();

    public void Add(Guid userId, string connectionId)
    {
        var userConnections = connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>());
        userConnections[connectionId] = 0;
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (!connections.TryGetValue(userId, out var userConnections))
        {
            return;
        }

        userConnections.TryRemove(connectionId, out _);
        if (userConnections.IsEmpty)
        {
            connections.TryRemove(userId, out _);
        }
    }

    public bool IsOnline(Guid userId) =>
        connections.TryGetValue(userId, out var userConnections) && !userConnections.IsEmpty;

    public IReadOnlyCollection<string> GetConnections(Guid userId) =>
        connections.TryGetValue(userId, out var userConnections)
            ? userConnections.Keys.ToArray()
            : [];
}
