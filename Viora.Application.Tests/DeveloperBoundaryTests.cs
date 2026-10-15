using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Viora.Infrastructure.Persistence.Migrations;
using viora_BE.Developer;
using Xunit;

public sealed class DeveloperBoundaryTests
{
    [Fact]
    public void MigrationOnlyAddsItsFiveTablesAndRollsThemBack()
    {
        var migration = new AddIndependentDeveloperAccounts();
        var allowed = new[] { "DeveloperAccounts", "DeveloperSessions", "DeveloperActionTokens", "DeveloperExternalIdentities", "DeveloperAuditLogs" };
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray(); Assert.Equal(5, tables.Length);
        Assert.All(migration.UpOperations, op => Assert.True(op is CreateTableOperation or CreateIndexOperation));
        Assert.All(tables, table => { Assert.Contains(table.Name, allowed); Assert.All(table.ForeignKeys, fk => Assert.Equal("DeveloperAccounts", fk.PrincipalTable)); });
        Assert.All(migration.DownOperations, op => Assert.Contains(Assert.IsType<DropTableOperation>(op).Name, allowed));
        Assert.Equal(5, migration.DownOperations.Count);
    }

    [Fact]
    public void RateLimitClientAttributionRequiresFreshMethodAndPathBoundHmac()
    {
        const string secret = "test-only-signing-material-not-a-production-secret";
        var context = new DefaultHttpContext(); context.Connection.RemoteIpAddress = System.Net.IPAddress.Loopback;
        context.Request.Method = "POST"; context.Request.Path = "/api/developer/auth/login";
        context.Request.Headers["X-ANKT-Bff-IP"] = "203.0.113.7";
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        context.Request.Headers["X-ANKT-Bff-Time"] = timestamp;
        context.Request.Headers["X-ANKT-Bff-Signature"] = Convert.ToHexString(HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), Encoding.UTF8.GetBytes($"203.0.113.7\n{timestamp}\nPOST\n/api/developer/auth/login")));
        Assert.Equal("203.0.113.7", DeveloperIngress.ClientIp(context, secret));
        context.Request.Path = "/api/developer/auth/register";
        Assert.Equal("127.0.0.1", DeveloperIngress.ClientIp(context, secret));
        Assert.Equal("127.0.0.1", DeveloperIngress.ClientIp(context, "incorrect-key"));
    }
}
