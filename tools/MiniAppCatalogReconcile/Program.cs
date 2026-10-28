using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

var targets = args.Where(x => x.StartsWith("--app-id=", StringComparison.Ordinal)).ToArray();
if (targets.Length != 1 || args.Count(x => x == "--apply") > 1 || args.Any(x => x != "--apply" && !x.StartsWith("--app-id=", StringComparison.Ordinal)))
{
    Console.Error.WriteLine("Usage: MiniAppCatalogReconcile --app-id=app_<43 characters> [--apply]; default is dry-run.");
    return 2;
}
var appId = targets[0]["--app-id=".Length..];
if (appId.Length != 47 || !appId.StartsWith("app_", StringComparison.Ordinal) || !appId.Skip(4).All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
{
    Console.Error.WriteLine("Invalid public app ID."); return 2;
}
// Deliberately never reads application appsettings/.env or supplies a default database.
var connection = Environment.GetEnvironmentVariable("ANKT_CATALOG_RECONCILE_CONNECTION");
if (string.IsNullOrWhiteSpace(connection))
{
    Console.Error.WriteLine("ANKT_CATALOG_RECONCILE_CONNECTION must explicitly select the operator-reviewed database."); return 2;
}
try
{
    await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connection, o => o.CommandTimeout(30)).Options);
    using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
    var result = await new MiniAppCatalogReconcileService(db, TimeProvider.System).ReconcileAsync(appId, args.Contains("--apply"), deadline.Token);
    Console.WriteLine(JsonSerializer.Serialize(result, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
    return result.Status is "unavailable" or "retry-required" ? 1 : 0;
}
catch (Exception)
{
    Console.Error.WriteLine("Catalog reconciliation failed; no credentials or provider details are printed."); return 1;
}
