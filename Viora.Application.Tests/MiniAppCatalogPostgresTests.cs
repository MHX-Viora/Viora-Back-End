using Microsoft.EntityFrameworkCore;
using Npgsql;
using Viora.Domain.Entities;
using Viora.Application.MiniApps;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.MiniApps;
using System.Text.Json;
using Xunit;

public sealed class MiniAppCatalogPostgresTests
{
    [MiniAppPostgresFact]
    public async Task ConcurrentPreferencesRespectUniqueAndAccountLimit()
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable("VIORA_MINI_APP_TEST_DB"));
        if (!(connection.Database ?? "").Contains("test", StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Dedicated test DB required.");
        var schema = "catalog_test_" + Guid.NewGuid().ToString("N");
        await using var admin = new NpgsqlConnection(connection.ConnectionString); await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE SCHEMA \"{schema}\"", admin)) await create.ExecuteNonQueryAsync();
        connection.SearchPath = schema; connection.Pooling = false;
        var options = new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection.ConnectionString).Options;
        try {
            await using var db = new AppDbContext(options); await db.Database.ExecuteSqlRawAsync(db.Database.GenerateCreateScript());
            var owner = new Account { Email = "owner@catalog.test" }; var developer = new Developer { AccountId = owner.Id, Name = "Partner", Email = owner.Email, Status = DeveloperStatus.Active };
            db.AddRange(owner, developer); var apps = Enumerable.Range(0, 101).Select(i => new MiniApp { DeveloperId = developer.Id, Name = $"App {i}", Slug = $"app-{i}", WebUrl = "https://example.com", CallbackUrl = "", ClientId = $"client-{i}", ClientSecretHash = "fixture-only", Status = MiniAppStatus.Active, PublishedVersion = 1, CatalogName = $"App {i}", CatalogSearchText = $"app {i}", PublishedConfigurationJson = JsonSerializer.Serialize(new MiniAppConfigurationInput($"App {i}", $"app-{i}", null, null, null, "https://example.com", "", ["example.com"], [])) }).ToArray();
            db.AddRange(apps); await db.SaveChangesAsync();
            async Task Add(Guid appId) { await using var context = new AppDbContext(options); await new MiniAppCatalogService(context).SetFavoriteAsync(owner.Id, appId, true, default); }
            await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => Add(apps[0].Id)));
            Assert.Equal(1, await db.MiniAppFavorites.CountAsync());
            db.AddRange(apps.Skip(1).Take(98).Select((app, index) => new MiniAppFavorite { AccountId = owner.Id, MiniAppId = app.Id, SortOrder = index + 1 })); await db.SaveChangesAsync();
            var outcomes = await Task.WhenAll(apps.Skip(99).Select(async app => { try { await Add(app.Id); return "ok"; } catch (MiniAppException e) { return e.Code; } }));
            Assert.Equal(1, outcomes.Count(result => result == "ok")); Assert.Contains("FAVORITES_LIMIT", outcomes);
            Assert.Equal(100, await db.MiniAppFavorites.CountAsync());
            await using var orderingDb = new AppDbContext(options);
            var ordering = new MiniAppCatalogService(orderingDb);
            var ids = await orderingDb.MiniAppFavorites.Where(f => f.AccountId == owner.Id).OrderBy(f => f.SortOrder).Select(f => f.MiniAppId).ToArrayAsync();
            Array.Reverse(ids); await ordering.ReorderFavoritesAsync(owner.Id, new(ids), default);
            Assert.Equal(ids.Take(60), (await ordering.GetCatalogAsync(owner.Id, new(Rail: "favorites", PageSize: 60), default)).Items.Select(a => a.Id));
            var stale = await Assert.ThrowsAsync<MiniAppException>(() => ordering.ReorderFavoritesAsync(owner.Id, new(ids.Skip(1).ToArray()), default));
            Assert.Equal("FAVORITES_CHANGED", stale.Code);
        } finally { await using var drop = new NpgsqlCommand($"DROP SCHEMA \"{schema}\" CASCADE", admin); await drop.ExecuteNonQueryAsync(); }
    }
}
