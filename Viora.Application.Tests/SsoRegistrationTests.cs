using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using OpenIddict.Server.AspNetCore;
using viora_BE.Sso;
using Xunit;

public sealed class SsoRegistrationTests
{
    [Fact]
    public async Task DisabledByDefaultPreservesAuthenticationAndRegistersNoServer()
    {
        var services = Services(new());
        await using var provider = services.BuildServiceProvider();
        Assert.Null(provider.GetService<IOpenIddictApplicationManager>());
        Assert.Equal("Bearer", (await provider.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync())!.Name);
    }

    [Theory]
    [InlineData("http://issuer.example")]
    [InlineData("/relative")]
    [InlineData("https://user:password@issuer.example")]
    [InlineData("https://issuer.example/?q=1")]
    [InlineData("https://issuer.example/#fragment")]
    [InlineData("")]
    public async Task EnabledServerRejectsInvalidIssuerAtStartup(string issuer)
    {
        using var host = Host(new() { ["Sso:Enabled"] = "true", ["Sso:Issuer"] = issuer });
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task EnabledServerRejectsMissingCertificatesAtStartup()
    {
        using var host = Host(new() { ["Sso:Enabled"] = "true", ["Sso:Issuer"] = "https://issuer.example" });
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Theory]
    [InlineData("Sso:AuthorizationCodeSeconds", "0")]
    [InlineData("Sso:AuthorizationCodeSeconds", "61")]
    [InlineData("Sso:AccessTokenSeconds", "0")]
    [InlineData("Sso:AccessTokenSeconds", "301")]
    public async Task EnabledServerRejectsUnsafeLifetimes(string key, string value)
    {
        using var certificates = new Certificates();
        var config = certificates.Config(); config[key] = value;
        using var host = Host(config);
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }

    [Fact]
    public async Task ValidConfigurationUsesLibraryCodeFlowS256AndIndependentKeys()
    {
        using var certificates = new Certificates();
        using var host = Host(certificates.Config());
        await host.StartAsync();
        var server = host.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        Assert.Equal("https://issuer.example/", server.Issuer!.AbsoluteUri);
        Assert.Equal([OpenIddictConstants.GrantTypes.AuthorizationCode], server.GrantTypes);
        Assert.True(server.RequireProofKeyForCodeExchange);
        Assert.Equal([OpenIddictConstants.CodeChallengeMethods.Sha256], server.CodeChallengeMethods);
        Assert.Equal(TimeSpan.FromSeconds(60), server.AuthorizationCodeLifetime);
        Assert.Equal(TimeSpan.FromMinutes(5), server.AccessTokenLifetime);
        Assert.NotEqual(Assert.Single(server.SigningCredentials).Key.KeyId,
            Assert.Single(server.EncryptionCredentials).Key.KeyId);
        Assert.False(server.DisableTokenStorage);
        Assert.False(server.EnableDegradedMode);
        Assert.False(host.Services.GetRequiredService<IOptionsMonitor<OpenIddictServerAspNetCoreOptions>>()
            .CurrentValue.DisableTransportSecurityRequirement);
        Assert.Equal("Bearer", (await host.Services.GetRequiredService<IAuthenticationSchemeProvider>()
            .GetDefaultAuthenticateSchemeAsync())!.Name);
        await host.StopAsync();
    }

    [Theory]
    [InlineData("public")]
    [InlineData("expired")]
    [InlineData("same")]
    [InlineData("missing")]
    [InlineData("password")]
    public async Task InvalidCredentialsFailWithoutLeakingPathOrPassword(string kind)
    {
        using var certificates = new Certificates();
        var config = certificates.Config();
        config["Sso:SigningCertificatePath"] = kind switch
        {
            "public" => certificates.PublicPath,
            "expired" => certificates.ExpiredPath,
            "same" => config["Sso:EncryptionCertificatePath"],
            "password" => config["Sso:SigningCertificatePath"],
            _ => Path.Combine(certificates.DirectoryPath, "missing.pfx")
        };
        if (kind == "password") config["Sso:SigningCertificatePassword"] = "incorrect-test-password";
        using var host = Host(config);
        var error = await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
        Assert.DoesNotContain(certificates.DirectoryPath, error.Message);
        Assert.DoesNotContain(Certificates.Password, error.Message);
        Assert.Null(error.InnerException);
    }

    private static ServiceCollection Services(Dictionary<string, string?> values)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddAuthentication("Bearer").AddJwtBearer();
        services.AddAnktSso(new ConfigurationBuilder().AddInMemoryCollection(values).Build());
        return services;
    }

    private static IHost Host(Dictionary<string, string?> values) => new HostBuilder()
        .ConfigureServices(services =>
        {
            foreach (var descriptor in Services(values)) services.Add(descriptor);
        }).Build();

    private sealed class Certificates : IDisposable
    {
        public const string Password = "isolated-test-password";
        public string DirectoryPath { get; } = Path.Combine(Path.GetTempPath(), "ankt-sso-" + Guid.NewGuid());
        public string PublicPath => Path.Combine(DirectoryPath, "public.cer");
        public string ExpiredPath => Path.Combine(DirectoryPath, "expired.pfx");
        public Certificates()
        {
            Directory.CreateDirectory(DirectoryPath);
            Write("signing", false); Write("encryption", false); Write("expired", true);
        }
        private void Write(string name, bool expired)
        {
            using var rsa = RSA.Create(2048);
            var request = new CertificateRequest("CN=isolated-" + name, rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-2), DateTimeOffset.UtcNow.AddDays(expired ? -1 : 2));
            File.WriteAllBytes(Path.Combine(DirectoryPath, name + ".pfx"), certificate.Export(X509ContentType.Pfx, Password));
            if (name == "signing") File.WriteAllBytes(PublicPath, certificate.Export(X509ContentType.Cert));
        }
        public Dictionary<string, string?> Config() => new()
        {
            ["Sso:Enabled"] = "true", ["Sso:Issuer"] = "https://issuer.example/",
            ["Sso:FirstPartyOrigin"] = "https://app.example",
            ["Sso:SigningCertificatePath"] = Path.Combine(DirectoryPath, "signing.pfx"),
            ["Sso:SigningCertificatePassword"] = Password,
            ["Sso:EncryptionCertificatePath"] = Path.Combine(DirectoryPath, "encryption.pfx"),
            ["Sso:EncryptionCertificatePassword"] = Password
        };
        public void Dispose() => Directory.Delete(DirectoryPath, true);
    }
}
