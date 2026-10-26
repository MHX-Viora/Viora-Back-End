using Microsoft.Extensions.Options;
using Microsoft.Extensions.DependencyInjection.Extensions;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Microsoft.AspNetCore.Authentication;
using System.Threading.RateLimiting;

namespace viora_BE.Sso;

public static class SsoRegistration
{
    public static IServiceCollection AddAnktSso(this IServiceCollection services, IConfiguration configuration)
    {
        Viora.Infrastructure.Developers.SandboxTestRegistration.AddSandboxTestIdentity(services, configuration);
        var section = configuration.GetSection(SsoOptions.SectionName);
        services.AddSingleton<IValidateOptions<SsoOptions>, SsoOptionsValidator>();
        services.AddSingleton<SsoCertificateProvisioningService>();
        services.AddOptions<SsoOptions>().Bind(section)
            .PostConfigure<SsoCertificateProvisioningService>((options, provisioning) => provisioning.Prepare(options))
            .ValidateOnStart();
        if (!section.GetValue<bool>(nameof(SsoOptions.Enabled))) return services;

        services.AddSingleton<SsoCredentials>();
        services.AddScoped<Viora.Infrastructure.Sso.MiniAppOidcRegistry>();
        services.AddScoped<Viora.Infrastructure.Sso.MiniAppPublicationRevocation>();
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<Viora.Infrastructure.Sso.SsoSessionService>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoBootstrapService>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoClientReader>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoConsentService>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoTokenService>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoAuthorizationOwnerService>();
        services.AddScoped<Viora.Infrastructure.Sso.SsoAccountLinkService>();
        services.AddScoped<SsoConsentInteraction>();
        services.AddScoped<SandboxProtocolObserver>();
        services.AddControllersWithViews();
        services.AddAntiforgery(options =>
        {
            options.Cookie.Name = "__Host-AnktSsoCsrf"; options.Cookie.Path = "/";
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.HttpOnly = true; options.Cookie.SameSite = SameSiteMode.Lax;
        });
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, SsoSessionHandler>(SsoSessionHandler.SchemeName, _ => { });
        services.AddRateLimiter(options => options.AddPolicy("sso", context => RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
        services.AddCors(options => options.AddPolicy("SsoBootstrap", policy => policy
            .WithOrigins(section[nameof(SsoOptions.FirstPartyOrigin)]?.TrimEnd('/') ?? "https://invalid.example")
            .WithMethods("POST").WithHeaders("Authorization", "Content-Type")));
        services.AddSingleton<Microsoft.AspNetCore.Cors.Infrastructure.ICorsPolicyProvider, SsoCorsPolicyProvider>();
        var openiddict = services.AddOpenIddict();
        openiddict.AddCore(core =>
        {
            core.DisableEntityCaching();
            core.UseEntityFrameworkCore().UseDbContext<Viora.Infrastructure.Persistence.AppDbContext>();
        });
        openiddict.AddServer(server =>
        {
            server.AddEventHandler<OpenIddictServerEvents.ProcessSignInContext>(handler=>handler
                .UseScopedHandler<SandboxProtocolObserver>()
                .SetOrder(OpenIddictServerHandlers.GenerateIdentityToken.Descriptor.Order+1_000));
            server.AddEventHandler<OpenIddictServerEvents.ValidateIntrospectionRequestContext>(handler => handler
                .UseScopedHandler<SsoIntrospectionPolicy>()
                .SetOrder(OpenIddictServerHandlers.Introspection.ValidateAuthorizedParty.Descriptor.Order + 1_000));
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
            server.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableUserInfoEndpointPassthrough();
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
                foreach (var certificate in credentials.SigningCertificates)
                    server.SigningCredentials.Add(new(new Microsoft.IdentityModel.Tokens.X509SecurityKey(certificate),
                        Microsoft.IdentityModel.Tokens.SecurityAlgorithms.RsaSha256));
                foreach (var certificate in credentials.EncryptionCertificates)
                    server.EncryptionCredentials.Add(new(new Microsoft.IdentityModel.Tokens.X509SecurityKey(certificate),
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
