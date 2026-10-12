using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Viora.Infrastructure.OpenApi;

// Protocol storage only; MiniApps, identities and consent remain in AppDbContext.
public sealed class OpenApiDbContext(DbContextOptions<OpenApiDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) { base.OnModelCreating(modelBuilder); modelBuilder.UseOpenIddict(); }
}
public sealed class OpenApiDbContextFactory : IDesignTimeDbContextFactory<OpenApiDbContext>
{
    public OpenApiDbContext CreateDbContext(string[] args)
    {
        var connection = Environment.GetEnvironmentVariable("VIORA_EF_DESIGN_CONNECTION") ?? throw new InvalidOperationException("Configure VIORA_EF_DESIGN_CONNECTION for migration tooling");
        return new(new DbContextOptionsBuilder<OpenApiDbContext>()
            .UseNpgsql(connection, postgres => postgres.MigrationsHistoryTable("__OpenApiMigrationsHistory"))
            .UseOpenIddict().Options);
    }
}
