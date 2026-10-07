using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore.Diagnostics;
using System.Data.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viora.Application.Configuration;
using Viora.Application.Chat;
using Viora.Application.Notifications;
using Viora.Application.Social;
using Viora.Domain.Entities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Persistence.Repositories;
using Xunit;

public sealed class ProfileSocialRegressionTests
{
    [Theory]
    [InlineData("GET")]
    [InlineData("POST")]
    [InlineData("DELETE")]
    [InlineData("PUT")]
    public void DeployedWebOriginCanPreflightSocialActions(string method)
    {
        var policy = new CorsPolicyBuilder()
            .WithOrigins(WebCorsOrigins.Resolve(["http://localhost:3000"]).ToArray())
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials().Build();
        var context = new DefaultHttpContext();
        context.Request.Method = "OPTIONS";
        context.Request.Headers.Origin = "https://mxh.ankt.vn";
        context.Request.Headers.AccessControlRequestMethod = method;
        context.Request.Headers.AccessControlRequestHeaders = "authorization,content-type";
        var service = new CorsService(Options.Create(new CorsOptions()), NullLoggerFactory.Instance);
        var result = service.EvaluatePolicy(context, policy);
        Assert.True(result.IsOriginAllowed);
        Assert.Equal("https://mxh.ankt.vn", result.AllowedOrigin);
        Assert.Contains(method, result.AllowedMethods);
        Assert.True(result.SupportsCredentials);
    }

    [Fact]
    public void UntrustedOriginsAreNotAllowed()
    {
        var origins = WebCorsOrigins.Resolve(["https://admin.example.com/", "https://bad.example/path", "*"]);
        Assert.Contains("https://admin.example.com", origins);
        Assert.DoesNotContain("*", origins);
        Assert.DoesNotContain("https://bad.example/path", origins);
        Assert.DoesNotContain("https://mxh.ankt.vn.evil.example", origins);
    }

    [Fact]
    public void NotificationUnauthorizedResponseStillIncludesCorsHeaders()
    {
        var policy = new CorsPolicyBuilder()
            .WithOrigins(WebCorsOrigins.Resolve(["https://admin.example.com"]).ToArray())
            .AllowAnyHeader().AllowAnyMethod().AllowCredentials().Build();
        var context = new DefaultHttpContext();
        context.Request.Method = "GET";
        context.Request.Path = "/api/notifications";
        context.Request.Headers.Origin = "https://mxh.ankt.vn";
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        var service = new CorsService(Options.Create(new CorsOptions()), NullLoggerFactory.Instance);
        service.ApplyResult(service.EvaluatePolicy(context, policy), context.Response);
        Assert.Equal("https://mxh.ankt.vn", context.Response.Headers.AccessControlAllowOrigin.ToString());
        Assert.Equal("true", context.Response.Headers.AccessControlAllowCredentials.ToString());
        Assert.Equal(StatusCodes.Status401Unauthorized, context.Response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FriendRequestIsPublishedOnlyAfterPersistenceAndDuplicateRequestDoesNotRepublish(bool dispatchFails)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        connection.CreateFunction("char_length", (string value) => value.Length);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var sender = new User { Account = new Account { Email = "sender@example.test", PasswordHash = "test", Status = AccountStatus.Active }, DisplayName = "Sender" };
        var recipient = new User { Account = new Account { Email = "recipient@example.test", PasswordHash = "test", Status = AccountStatus.Active }, DisplayName = "Recipient" };
        db.AddRange(sender, recipient); await db.SaveChangesAsync();
        var publisher = new RecordingNotifications(db, dispatchFails);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<INotificationService>(publisher)
            .AddSingleton<ILogger<SocialRepository>>(NullLogger<SocialRepository>.Instance).BuildServiceProvider();
        var repository = ActivatorUtilities.CreateInstance<SocialRepository>(services);
        var handler = new SendFriendRequestHandler(repository, new SendFriendRequestValidator());
        var request = new SendFriendRequestCommand(sender.Id, recipient.Id);
        Assert.True((await handler.Handle(request, default)).IsSuccess);
        var persisted = Assert.Single(await db.Notifications.ToListAsync());
        Assert.Equal(recipient.Id, persisted.UserId);
        Assert.Equal(NotificationType.FriendRequest, persisted.NotificationType);
        Assert.Equal(persisted.Id, Assert.Single(publisher.Published).Id);
        Assert.True(publisher.WasCommitted);
        Assert.False((await handler.Handle(request, default)).IsSuccess);
        Assert.Single(publisher.Published);
        Assert.Single(await db.Notifications.ToListAsync());
    }

    [Fact]
    public async Task RolledBackSocialActionDoesNotPublishNotification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction("char_length", (string value) => value.Length);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var publisher = new RecordingNotifications(db);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<INotificationService>(publisher)
            .AddSingleton<ILogger<SocialRepository>>(NullLogger<SocialRepository>.Instance).BuildServiceProvider();
        var repository = ActivatorUtilities.CreateInstance<SocialRepository>(services);
        await Assert.ThrowsAsync<InvalidOperationException>(() => repository.ExecuteInTransactionAsync(async token =>
        {
            await repository.AddNotificationAsync(new Notification { Title = "test" }, token);
            throw new InvalidOperationException("rollback");
        }, default));
        Assert.Empty(publisher.Published);
        Assert.Empty(await db.Notifications.AsNoTracking().ToListAsync());
    }

    [Theory]
    [InlineData(FriendshipStatus.Accepted, false, true)]
    [InlineData(FriendshipStatus.Pending, false, false)]
    [InlineData(null, false, false)]
    [InlineData(null, true, true)]
    public async Task ChatPermissionMatchesProfileFriendshipRules(FriendshipStatus? status, bool allowEveryone, bool permitted)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction("char_length", (string value) => value.Length);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection)
            .AddInterceptors(new SkipPostgresPairLock()).Options);
        await db.Database.EnsureCreatedAsync();
        var sender = new User { Account = new Account { Email = "chat-sender@example.test", Status = AccountStatus.Active }, DisplayName = "Sender" };
        var recipient = new User { Account = new Account { Email = "chat-recipient@example.test", Status = AccountStatus.Active }, DisplayName = "Recipient" };
        db.AddRange(sender, recipient, new UserSettings { UserId = recipient.Id, AllowMessageEveryone = allowEveryone });
        if (status.HasValue) db.Add(new Friendship { RequesterUserId = recipient.Id, AddresseeUserId = sender.Id, Status = status.Value });
        await db.SaveChangesAsync();
        var repository = new ChatConversationRepository(db, NullLogger<ChatConversationRepository>.Instance);
        var result = await repository.CreatePrivateConversationAsync(new CreatePrivateConversationCommand(sender.Id, recipient.Id), default);
        Assert.Equal(permitted, result.IsSuccess);
        Assert.Equal(permitted ? 1 : 0, await db.Conversations.CountAsync());
        if (!permitted) Assert.Equal(ChatError.Forbidden, result.Error);
    }

    [Fact]
    public async Task UnfollowRemovesRelationshipWithoutSendingAnotherFollowNotification()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction("char_length", (string value) => value.Length);
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(connection).Options);
        await db.Database.EnsureCreatedAsync();
        var sender = new User { Account = new Account { Email = "follow-sender@example.test", Status = AccountStatus.Active }, DisplayName = "Sender" };
        var recipient = new User { Account = new Account { Email = "follow-recipient@example.test", Status = AccountStatus.Active }, DisplayName = "Recipient" };
        db.AddRange(sender, recipient); await db.SaveChangesAsync();
        var publisher = new RecordingNotifications(db);
        using var services = new ServiceCollection().AddSingleton(db).AddSingleton<INotificationService>(publisher)
            .AddSingleton<ILogger<SocialRepository>>(NullLogger<SocialRepository>.Instance).BuildServiceProvider();
        var handler = new ToggleFollowHandler(ActivatorUtilities.CreateInstance<SocialRepository>(services), new ToggleFollowValidator());
        var request = new ToggleFollowCommand(sender.Id, recipient.Id);
        var followed = await handler.Handle(request, default);
        Assert.True(followed.Value!.IsFollowing);
        Assert.Single(publisher.Published);
        var unfollowed = await handler.Handle(request, default);
        Assert.False(unfollowed.Value!.IsFollowing);
        Assert.Equal(0, unfollowed.Value.FollowerCount);
        Assert.Empty(await db.Follows.ToListAsync());
        Assert.Single(publisher.Published);
    }

    // SQLite exercises real permissions and persistence; the PostgreSQL-specific
    // concurrency lock is outside this test's scope.
    private sealed class SkipPostgresPairLock : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(command.CommandText.StartsWith("SELECT pg_advisory_xact_lock", StringComparison.Ordinal)
                ? InterceptionResult<int>.SuppressWithResult(0) : result);
    }

    private sealed class RecordingNotifications(AppDbContext db, bool dispatchFails = false) : INotificationService
    {
        public List<Notification> Published { get; } = [];
        public bool WasCommitted { get; private set; }
        public Task<Notification> SendAsync(SendNotificationCommand command, CancellationToken token) => throw new NotSupportedException();
        public async Task PublishAsync(Notification notification, CancellationToken token)
        {
            WasCommitted = db.Database.CurrentTransaction?.GetDbTransaction().Connection is null
                && await db.Notifications.AsNoTracking().AnyAsync(n => n.Id == notification.Id, token);
            Published.Add(notification);
            if (dispatchFails) throw new InvalidOperationException("Realtime unavailable");
        }
    }
}
