using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Security;
using Xunit;

public sealed class MiniAppHybridPostgresTests
{
    [MiniAppPostgresFact]
    public async Task ConcurrentRuntimeLaunchesHaveSeparateSessionsAndCloseIsIdempotent()
    {
        var builder = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VIORA_MINI_APP_TEST_DB"));
        if (!(builder.Database ?? "").Contains("test", StringComparison.OrdinalIgnoreCase) && !(builder.Database ?? "").Contains("audit", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Dedicated test database required.");
        var schema = "mini_app_test_" + Guid.NewGuid().ToString("N");
        await using var connection = new NpgsqlConnection(builder.ConnectionString); await connection.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", connection)) await create.ExecuteNonQueryAsync();
        builder.SearchPath = schema; builder.Pooling = false;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(builder.ConnectionString).Options;
        try
        {
            await using var setup = new AppDbContext(options); await setup.Database.ExecuteSqlRawAsync(setup.Database.GenerateCreateScript());
            var credentials = new ClientCredentialService(); var secret = credentials.CreateSecret();
            var account = new Account { Email = "miniapp@example.com" };
            var developer = new Developer { Name = "Test developer", Email = "developer@example.com", AccountId = account.Id, Status = DeveloperStatus.Active };
            var app = new MiniApp { Name = "Concurrent app", Slug = "concurrent-app", DeveloperId = developer.Id, Status = MiniAppStatus.Active, WebUrl = "https://example.com/app", CallbackUrl = "https://example.com/callback", CallbackUrls = ["https://example.com/callback"], AllowedDomains = ["example.com"], AllowedOrigins = ["https://example.com"], AuthenticationMode = MiniAppAuthenticationMode.AnktSso, ClientId = credentials.CreateClientId(), ClientSecretHash = credentials.HashSecret(secret) };
            MiniAppTestPublication.Publish(app, "identity.login");
            setup.AddRange(account, developer, app, new MiniAppPermissionMapping { MiniAppId = app.Id, PermissionId = Guid.Parse("11111111-1111-1111-1111-111111111101") },
                new MiniAppVerifiedDomain { MiniAppId = app.Id, Host = "example.com", ChallengeToken = "test-proof", VerifiedAt = DateTime.UtcNow }); await setup.SaveChangesAsync();
            var service = new MiniAppService(setup, credentials); await service.GrantConsentAsync(account.Id, app.Id, new(["identity.login"], true), default);
            async Task<LaunchMiniAppResponse> Launch()
            {
                await using var db = new AppDbContext(options);
                return await new MiniAppService(db, credentials).LaunchAsync(account.Id, app.Id, default);
            }
            var launches = await Task.WhenAll(Launch(), Launch());
            Assert.NotEqual(launches[0].SessionId, launches[1].SessionId);
            Assert.NotEqual(launches[0].SessionToken, launches[1].SessionToken);
            var first = new ValidateMiniAppSessionRequest(launches[0].SessionId!.Value, launches[0].SessionToken!);
            var second = new ValidateMiniAppSessionRequest(launches[1].SessionId!.Value, launches[1].SessionToken!);
            async Task Close()
            {
                await using var db = new AppDbContext(options);
                await new MiniAppService(db, credentials).CloseSessionAsync(account.Id, app.Id, first, default);
            }
            await Task.WhenAll(Close(), Close());
            Assert.False((await service.ValidateSessionAsync(account.Id, app.Id, first, default)).Active);
            Assert.True((await service.ValidateSessionAsync(account.Id, app.Id, second, default)).Active);
            await service.RevokeConsentAsync(account.Id, app.Id, default);
            Assert.Empty((await service.ValidateSessionAsync(account.Id, app.Id, second, default)).Permissions);
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }
}
public sealed class MiniAppPostgresFactAttribute : FactAttribute
{
    public MiniAppPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VIORA_MINI_APP_TEST_DB"))) Skip = "VIORA_MINI_APP_TEST_DB is not configured; PostgreSQL concurrency not verified."; }
}
