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
});
builder.Logging.ClearProviders(); builder.Logging.AddSimpleConsole(); builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:5318");
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
var app = builder.Build();
using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
app.UseDeveloperAccountBoundary(); app.UseAuthentication(); app.UseRateLimiter(); app.UseAuthorization();
app.MapDeveloperEndpoints(); app.MapDeveloperAccountEndpoints();
Console.WriteLine("Developer E2E host: http://127.0.0.1:5318 (isolated disposable SQLite database)");
try { await app.RunAsync(); }
finally
{
    SqliteConnection.ClearAllPools();
    if (!directory.StartsWith(testRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Unexpected test database directory.");
    try { Directory.Delete(directory, recursive: true); } catch (IOException) { /* OS may retain a diagnostic file handle. */ }
}
