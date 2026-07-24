using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Viora.Application.Realtime;
using Viora.Infrastructure;
using Viora.Infrastructure.Realtime;
using Xunit;

namespace Viora.Infrastructure.Tests;

public sealed class FirebasePushConfigurationTests
{
    [Fact]
    public void Infrastructure_registers_firebase_push_sender()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=viora;Username=test",
                ["Jwt:Key"] = "test-signing-key-with-at-least-32-bytes",
                ["Cloudinary:CloudName"] = "cloud",
                ["Cloudinary:ApiKey"] = "key",
                ["Cloudinary:ApiSecret"] = "secret"
            })
            .Build();

        services.AddInfrastructure(configuration);

        var descriptor = Assert.Single(services, service => service.ServiceType == typeof(IPushNotificationSender));
        Assert.Equal(typeof(FirebasePushNotificationSender), descriptor.ImplementationType);
    }

    [Fact]
    public void Firebase_initializer_returns_null_without_service_account()
    {
        var initializer = new FirebaseInitializer(
            Options.Create(new FirebaseOptions()),
            new FakeHostEnvironment(),
            NullLogger<FirebaseInitializer>.Instance);

        Assert.Null(initializer.GetApp());
    }

    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Development;
        public string ApplicationName { get; set; } = "Viora.Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
