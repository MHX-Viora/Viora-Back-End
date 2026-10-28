using Microsoft.EntityFrameworkCore;
using Viora.Infrastructure.Developers;
using Viora.Infrastructure.Persistence;

namespace viora_BE.Developer;

public static class MiniAppCredentialImportCommand
{
    public static async Task<int> RunAsync(IConfiguration configuration, CancellationToken ct = default)
    {
        try {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(o => o.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));
            services.Configure<MiniAppOptions>(configuration.GetSection("MiniApps"));
            services.AddSingleton(TimeProvider.System);
            services.AddScoped<MiniAppSecretProtector>(); services.AddScoped<MiniAppPartnerCredentialStore>();
            await using var provider = services.BuildServiceProvider();
            await using var scope = provider.CreateAsyncScope();
            var options = configuration.GetSection("MiniAppAuth").Get<MiniAppBridgeOptions>() ?? new();
            var count = await scope.ServiceProvider.GetRequiredService<MiniAppPartnerCredentialStore>().ImportLegacyAsync(options, ct);
            Console.WriteLine($"Imported {count} Mini App credential(s). Existing database credentials were not overwritten.");
            return 0;
        }
        catch (Exception) {
            // Connection strings and decrypted legacy credentials must never enter logs.
            Console.Error.WriteLine("Credential import failed. Check database migration, encryption keys, unique Client IDs and legacy secret/hash pairs. No credentials were printed.");
            return 1;
        }
    }
}
