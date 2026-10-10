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
    public async Task ConcurrentRedemptionConsumesOnceAndConcurrentFirstLoginSharesOnePairwiseIdentity()
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
            setup.AddRange(account, developer, app, new MiniAppPermissionMapping { MiniAppId = app.Id, PermissionId = Guid.Parse("11111111-1111-1111-1111-111111111101") },
                new MiniAppVerifiedDomain { MiniAppId = app.Id, Host = "example.com", ChallengeToken = "test-proof", VerifiedAt = DateTime.UtcNow }); await setup.SaveChangesAsync();
            var service = new MiniAppService(setup, credentials); await service.GrantConsentAsync(account.Id, app.Id, new(["identity.login"], true), default);
            var launch = await service.LaunchAsync(account.Id, app.Id, default);
            var auth = new AuthorizeMiniAppRequest(launch.SessionId!.Value, launch.SessionToken!, app.CallbackUrl, MiniAppHybridIntegrationTests.Fixture.State, MiniAppSecurityPolicy.CreatePkceChallenge(MiniAppHybridIntegrationTests.Fixture.Verifier), "S256");
            async Task<string> Code() => MiniAppHybridIntegrationTests.Code(await service.AuthorizeAsync(account.Id, app.Id, auth, default));
            async Task<ExchangeLaunchCodeResponse?> Redeem(string code, Task gate)
            {
                await using var db = new AppDbContext(options); await gate;
                try { return await new MiniAppService(db, credentials).ExchangeAsync(new(app.ClientId, secret, code, app.CallbackUrl, auth.State, MiniAppHybridIntegrationTests.Fixture.Verifier), default); }
                catch (MiniAppException e) when (e.Code == MiniAppErrorCodes.LaunchCodeUsed) { return null; }
            }
            var firstCode = await Code(); var firstGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var a = Redeem(firstCode, firstGate.Task); var b = Redeem(firstCode, firstGate.Task); firstGate.SetResult();
            Assert.Single((await Task.WhenAll(a, b)).OfType<ExchangeLaunchCodeResponse>());
            await setup.MiniAppExternalIdentities.ExecuteDeleteAsync();
            var codeA = await Code(); var codeB = await Code(); var distinctGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var c = Redeem(codeA, distinctGate.Task); var d = Redeem(codeB, distinctGate.Task); distinctGate.SetResult();
            var identities = await Task.WhenAll(c, d); Assert.NotNull(identities[0]); Assert.NotNull(identities[1]); Assert.Equal(identities[0]!.Subject, identities[1]!.Subject);
            Assert.Equal(1, await setup.MiniAppExternalIdentities.CountAsync());
        }
        finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", connection); await drop.ExecuteNonQueryAsync(); }
    }
}
public sealed class MiniAppPostgresFactAttribute : FactAttribute
{
    public MiniAppPostgresFactAttribute() { if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VIORA_MINI_APP_TEST_DB"))) Skip = "VIORA_MINI_APP_TEST_DB is not configured; PostgreSQL concurrency not verified."; }
}
