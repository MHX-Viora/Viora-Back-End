using System.Security.Claims;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viora.Application.Live;
using Viora.Domain.Entities;
using Viora.Infrastructure.LiveStreaming;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Realtime;
using Xunit;

public sealed class LivePresentationTests
{
    [Fact]
    public void RejectsUnboundedOrMalformedPresentation()
    {
        var p = new LivePresentation(1, 1280, 720, true, .9, .1);
        Assert.True(p.IsValid);
        Assert.False((p with { Version = 2 }).IsValid);
        Assert.False((p with { Width = 0 }).IsValid);
        Assert.False((p with { Height = 8192 }).IsValid);
        Assert.False((p with { Width = 1279 }).IsValid);
        Assert.False((p with { CameraX = double.NaN }).IsValid);
        Assert.False((p with { CameraY = 1.1 }).IsValid);
    }

    [Fact]
    public async Task OnlyLiveHostCanUpdateOrClearCachedPresentation()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        await db.Database.ExecuteSqlRawAsync("PRAGMA foreign_keys=OFF");
        var host = Guid.NewGuid();
        var live = new Live { Id = Guid.NewGuid(), HostUserId = host, CategoryId = Guid.NewGuid(), Title = "Test", AgoraChannelName = "test", Status = LiveStatus.Live };
        db.Lives.Add(live); await db.SaveChangesAsync();
        using var cache = new MemoryCache(new MemoryCacheOptions());
        var clients = new HubClientsStub();
        var hub = new RealtimeHub(new ConnectionRegistry(), db, new InMemoryLiveCommentBuffer(Options.Create(new LiveChatOptions())), new InMemoryLiveReactionBuffer(), Options.Create(new LiveChatOptions()), NullLogger<RealtimeHub>.Instance, cache)
        { Context = new Caller(host), Clients = clients };
        var p = new LivePresentation(1, 1280, 720, true, .9, .1);
        await hub.SetLivePresentation(live.Id, p);
        Assert.Equal(p, cache.Get<LivePresentation>($"live-presentation:{live.Id}"));
        Assert.Equal("LivePresentation", clients.Proxy.Event);
        Assert.Equal($"live:{live.Id:N}", clients.GroupName);
        hub.Context = new Caller(Guid.NewGuid());
        await Assert.ThrowsAsync<HubException>(() => hub.SetLivePresentation(live.Id, null));
        Assert.Equal(p, cache.Get<LivePresentation>($"live-presentation:{live.Id}"));
        hub.Context = new Caller(host);
        await Assert.ThrowsAsync<HubException>(() => hub.SetLivePresentation(live.Id, p with { CameraX = double.PositiveInfinity }));
        await hub.SetLivePresentation(live.Id, null);
        Assert.Null(cache.Get<LivePresentation>($"live-presentation:{live.Id}"));
        live.Status = LiveStatus.Ended; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<HubException>(() => hub.SetLivePresentation(live.Id, p));
    }

    private sealed class Caller(Guid id) : HubCallerContext
    {
        public override string ConnectionId => "test";
        public override string? UserIdentifier => id.ToString();
        public override ClaimsPrincipal User => new(new ClaimsIdentity([new Claim("user_id", id.ToString())], "test"));
        public override IDictionary<object, object?> Items { get; } = new Dictionary<object, object?>();
        public override IFeatureCollection Features { get; } = new FeatureCollection();
        public override CancellationToken ConnectionAborted => default;
        public override void Abort() { }
    }
    private sealed class Proxy : IClientProxy
    {
        public string? Event;
        public Task SendCoreAsync(string method, object?[] args, CancellationToken cancellationToken = default) { Event = method; return Task.CompletedTask; }
    }
    private sealed class HubClientsStub : IHubCallerClients
    {
        public readonly Proxy Proxy = new();
        public string? GroupName;
        public IClientProxy All => Proxy;
        public IClientProxy Caller => Proxy;
        public IClientProxy Others => Proxy;
        public IClientProxy AllExcept(IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Client(string id) => Proxy;
        public IClientProxy Clients(IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Group(string name) { GroupName = name; return Proxy; }
        public IClientProxy GroupExcept(string name, IReadOnlyList<string> ids) => Proxy;
        public IClientProxy Groups(IReadOnlyList<string> names) => Proxy;
        public IClientProxy OthersInGroup(string name) => Proxy;
        public IClientProxy User(string id) => Proxy;
        public IClientProxy Users(IReadOnlyList<string> ids) => Proxy;
    }
}
