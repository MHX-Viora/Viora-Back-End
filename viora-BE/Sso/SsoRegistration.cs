using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;

namespace viora_BE.Sso;

public static class SsoRegistration
{
    public static IServiceCollection AddAnktSso(this IServiceCollection services, IConfiguration configuration)
    {
        var section = configuration.GetSection(SsoOptions.SectionName);
        services.AddSingleton<IValidateOptions<SsoOptions>, SsoOptionsValidator>();
        services.AddOptions<SsoOptions>().Bind(section).ValidateOnStart();
        if (!section.GetValue<bool>(nameof(SsoOptions.Enabled))) return services;

        services.AddSingleton<SsoCredentials>();
        var openiddict = services.AddOpenIddict();
        openiddict.AddCore(); // Persistent store integration follows in Task 2.
        openiddict.AddServer(server =>
        {
            server.SetAuthorizationEndpointUris("/connect/authorize")
                .SetTokenEndpointUris("/connect/token")
                .SetUserInfoEndpointUris("/connect/userinfo")
                .SetRevocationEndpointUris("/connect/revocation")
                .SetIntrospectionEndpointUris("/connect/introspect")
                .SetJsonWebKeySetEndpointUris("/.well-known/jwks.json")
                .AllowAuthorizationCodeFlow()
                .RequireProofKeyForCodeExchange()
                .UseReferenceAccessTokens()
                .RegisterScopes(OpenIddictConstants.Scopes.OpenId, OpenIddictConstants.Scopes.Profile, OpenIddictConstants.Scopes.Email);
            server.UseAspNetCore(); // Retain HTTPS enforcement and the existing default scheme.
        });
        services.AddOptions<OpenIddictServerOptions>()
            .Configure<IOptions<SsoOptions>, SsoCredentials>((server, configured, credentials) =>
            {
                var options = configured.Value;
                server.Issuer = new Uri(options.Issuer, UriKind.Absolute);
                server.AuthorizationCodeLifetime = TimeSpan.FromSeconds(options.AuthorizationCodeSeconds);
                server.AccessTokenLifetime = TimeSpan.FromSeconds(options.AccessTokenSeconds);
                server.CodeChallengeMethods.Clear();
                server.CodeChallengeMethods.Add(OpenIddictConstants.CodeChallengeMethods.Sha256);
                server.SigningCredentials.Add(new(new Microsoft.IdentityModel.Tokens.X509SecurityKey(credentials.Signing),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256));
                server.EncryptionCredentials.Add(new(new Microsoft.IdentityModel.Tokens.X509SecurityKey(credentials.Encryption),
                    Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaOAEP, Microsoft.IdentityModel.Tokens.SecurityAlgorithms.Aes256CbcHmacSha512));
            });
        services.AddHostedService<SsoStartupValidation>();
        return services;
    }

    private sealed class SsoStartupValidation(IOptionsMonitor<OpenIddictServerOptions> server) : IHostedService
    {
        public Task StartAsync(CancellationToken cancellationToken) { _ = server.CurrentValue; return Task.CompletedTask; }
        public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
