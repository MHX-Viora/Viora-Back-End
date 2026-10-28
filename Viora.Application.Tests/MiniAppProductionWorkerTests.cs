using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Viora.Infrastructure.Developers;
using viora_BE.Production;
using Xunit;

public sealed class MiniAppProductionWorkerTests
{
    [Theory]
    [InlineData(false, false, 0)]
    [InlineData(true, false, 1)]
    [InlineData(false, true, 1)]
    public void CleanupRegistrationIsExplicitAndIdempotent(bool enabled, bool cleanup, int expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        { ["MiniAppRuntime:Enabled"] = enabled.ToString(), ["MiniAppRuntime:CleanupEnabled"] = cleanup.ToString() }).Build();
        var services = new ServiceCollection();
        services.AddMiniAppProductionServices(configuration);
        services.AddMiniAppProductionWorker(configuration); services.AddMiniAppProductionWorker(configuration);
        Assert.Equal(expected, services.Count(x => x.ImplementationType == typeof(MiniAppProductionWorker)));
    }
    [Fact]
    public void CleanupOnlyConfigurationStillRequiresBoundedRetention()
    {
        var validator = new MiniAppProductionOptionsValidator(new());
        Assert.True(validator.Validate(null, new() { CleanupEnabled = true, EventRetentionDays = 0 }).Failed);
        Assert.True(validator.Validate(null, new() { CleanupEnabled = true, EventRetentionDays = 31 }).Failed);
        Assert.True(validator.Validate(null, new() { CleanupEnabled = true, EventRetentionDays = 7 }).Succeeded);
    }
}
