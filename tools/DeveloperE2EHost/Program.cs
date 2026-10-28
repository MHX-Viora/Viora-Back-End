using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Persistence;
using viora_BE.Developer;

// A loopback-only integration host. It never loads application settings or the production connection string.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development", Args = [] });
builder.Configuration.Sources.Clear();
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
{
    ["DeveloperAuth:PortalUrl"] = "http://127.0.0.1:3100/developers",
    ["DeveloperAuth:SmtpHost"] = "127.0.0.1", ["DeveloperAuth:SmtpPort"] = "2526",
    ["DeveloperAuth:SmtpFrom"] = "developer@ankt.example", ["DeveloperAuth:AllowInsecureLocalSmtp"] = "true",
    ["MiniApps:ActiveKeyVersion"] = "ephemeral-e2e",
    ["MiniApps:EncryptionKeys:ephemeral-e2e"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)),
});
// Only explicitly supplied storage settings are read. Never load a production database or developer secret key.
builder.Configuration.AddEnvironmentVariables("ANKT_E2E_");
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole(); builder.Logging.SetMinimumLevel(LogLevel.Warning);
var hostPort = int.TryParse(Environment.GetEnvironmentVariable("ANKT_E2E_HOST_PORT"), out var selectedPort) && selectedPort is > 1024 and <= 65535 ? selectedPort : 5318;
builder.WebHost.UseUrls($"http://127.0.0.1:{hostPort}");
var testRoot = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "ankt-developer-e2e"));
var directory = Path.GetFullPath(Path.Combine(testRoot, Guid.NewGuid().ToString("N"))); Directory.CreateDirectory(directory);
var dbPath = Path.Combine(directory, "developer.sqlite");
builder.Services.AddDbContext<AppDbContext>(options =>
{
    var connection = new SqliteConnection($"Data Source={dbPath}");
    connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
    options.UseSqlite(connection, contextOwnsConnection: true);
});
builder.Services.AddDeveloperAccounts(builder.Configuration);
builder.Services.AddDeveloperMiniApps(builder.Configuration);
await using var domainSite=Environment.GetEnvironmentVariable("ANKT_E2E_DOMAIN_FIXTURE")=="1"?await ControlledDomainSite.Start():null;
if(domainSite is not null)builder.Services.AddSingleton<Viora.Application.Developers.IPublicWebFetcher>(domainSite.Fetcher());
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
app.UseDeveloperAccountBoundary(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.MapDeveloperEndpoints(); app.MapDeveloperAccountEndpoints(); app.MapDeveloperMiniApps();
var stopKey = Environment.GetEnvironmentVariable("ANKT_E2E_STOP_KEY");
if (!string.IsNullOrEmpty(stopKey)) app.MapPost("/_fixture/stop", (HttpContext http, IHostApplicationLifetime lifetime) => {
    if (!System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(http.Request.Headers["X-Fixture-Key"].ToString()), System.Text.Encoding.UTF8.GetBytes(stopKey))) return Results.NotFound();
    lifetime.StopApplication(); return Results.NoContent();
});
Console.WriteLine($"Developer E2E host: http://127.0.0.1:{hostPort} (isolated disposable SQLite database)");
// Keep the service provider alive until external test assets have been cleaned up.
try { await app.StartAsync(); await app.WaitForShutdownAsync(); }
finally
{
    // Remove only assets created by this disposable fixture before discarding their upload tickets.
    using (var cleanupScope = app.Services.CreateScope())
    {
        var cleanupDb = cleanupScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var storage = cleanupScope.ServiceProvider.GetRequiredService<Viora.Infrastructure.Developers.MiniAppLogoStorage>();
        foreach (var logo in await cleanupDb.MiniAppLogoUploads.AsNoTracking().ToListAsync())
        {
            try { await storage.Delete(logo.PublicId, CancellationToken.None); }
            catch { Console.Error.WriteLine("E2E Cloudinary cleanup failed; retain the fixture database to retry cleanup."); throw; }
        }
    }
    await app.DisposeAsync();
    SqliteConnection.ClearAllPools();
    if (!directory.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test database directory.");
    try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* OS may retain a diagnostic file handle. */ }
}
