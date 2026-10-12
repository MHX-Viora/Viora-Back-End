using System.Net;
using System.Threading.RateLimiting;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Validation.AspNetCore;
using OpenIddict.EntityFrameworkCore.Models;
using Viora.Infrastructure.OpenApi;

namespace viora_BE.OpenApi;

public sealed class PartnerOpenApiOptions
{
    public bool Enabled { get; set; }
    public string Issuer { get; set; } = "";
    public string Audience { get; set; } = "";
    public string AuthorizationUi { get; set; } = "";
    public string? SigningCertificatePath { get; set; }
    public string? EncryptionCertificatePath { get; set; }
    public string? CertificatePassword { get; set; }
    public string[] KnownProxies { get; set; } = [];
}

public static class PartnerOpenApiConfiguration
{
    public static PartnerOpenApiOptions AddPartnerOpenApi(this WebApplicationBuilder builder, Action<DbContextOptionsBuilder>? configureDatabase = null)
    {
        var settings = builder.Configuration.GetSection("OpenApi").Get<PartnerOpenApiOptions>() ?? new();
        builder.Services.AddSingleton(settings);
        if (!settings.Enabled) return settings;
        static Uri Address(string value, bool development)
        {
            if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.Scheme != "https" && !(development && uri.Scheme == "http" && uri.IsLoopback)) throw new InvalidOperationException("Configure valid HTTPS OpenApi origins");
            return uri;
        }
        var issuer = Address(settings.Issuer, builder.Environment.IsDevelopment());
        Address(settings.AuthorizationUi, builder.Environment.IsDevelopment());
        if (issuer.AbsolutePath != "/" || string.IsNullOrWhiteSpace(settings.Audience)) throw new InvalidOperationException("Configure OpenApi issuer origin and audience");
        if (settings.KnownProxies.Length == 0) throw new InvalidOperationException("Configure explicit OpenApi KnownProxies; forwarding headers must not trust all senders");
        builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.Warning);
        builder.Logging.AddFilter("OpenIddict", LogLevel.Warning);
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownNetworks.Clear(); options.KnownProxies.Clear();
            foreach (var proxy in settings.KnownProxies) options.KnownProxies.Add(IPAddress.Parse(proxy));
        });
        builder.Services.AddDbContext<OpenApiDbContext>(options =>
        {
            if (configureDatabase is null) options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"), pg => pg.MigrationsHistoryTable("__OpenApiMigrationsHistory")); else configureDatabase(options);
            options.UseOpenIddict();
        });
        builder.Services.AddScoped<OpenApiAccessService>();
        builder.Services.AddSingleton<OpenApiPrincipalLimiter>();
        builder.Services.AddHostedService<OpenApiTokenPruningService>();
        builder.Services.AddOpenIddict()
            .AddCore(options => { options.UseEntityFrameworkCore().UseDbContext<OpenApiDbContext>(); options.ReplaceApplicationManager<OpenIddictEntityFrameworkCoreApplication, MiniAppOAuthApplicationManager>(); options.DisableEntityCaching(); })
            .AddServer(options =>
            {
                options.SetIssuer(issuer);
                options.SetAuthorizationEndpointUris("/oauth/authorize").SetTokenEndpointUris("/oauth/token").SetRevocationEndpointUris("/oauth/revoke").SetJsonWebKeySetEndpointUris("/oauth/jwks").SetUserInfoEndpointUris("/oidc/userinfo");
                options.AllowAuthorizationCodeFlow().RequireProofKeyForCodeExchange();
                options.SetAuthorizationCodeLifetime(TimeSpan.FromSeconds(60)).SetAccessTokenLifetime(TimeSpan.FromMinutes(5)).SetIdentityTokenLifetime(TimeSpan.FromMinutes(5));
                options.RegisterScopes(OpenApiPolicy.ScopePermissions.Keys.ToArray());
                options.RegisterAudiences(settings.Audience);
                options.DisableAccessTokenEncryption();
                if (builder.Environment.IsDevelopment()) { options.AddDevelopmentSigningCertificate().AddDevelopmentEncryptionCertificate(); }
                else
                {
                    if (string.IsNullOrWhiteSpace(settings.SigningCertificatePath) || string.IsNullOrWhiteSpace(settings.EncryptionCertificatePath)) throw new InvalidOperationException("Provide persistent OpenApi signing and encryption certificates");
                    options.AddSigningCertificate(new X509Certificate2(settings.SigningCertificatePath, settings.CertificatePassword));
                    options.AddEncryptionCertificate(new X509Certificate2(settings.EncryptionCertificatePath, settings.CertificatePassword));
                }
                var integration = options.UseAspNetCore().EnableAuthorizationEndpointPassthrough().EnableTokenEndpointPassthrough().EnableUserInfoEndpointPassthrough();
                if (builder.Environment.IsDevelopment()) integration.DisableTransportSecurityRequirement();
            })
            .AddValidation(options => { options.UseLocalServer(); options.AddAudiences(settings.Audience); options.EnableTokenEntryValidation(); options.UseAspNetCore(); });
        builder.Services.AddAuthorization(options => options.AddPolicy("OpenApi", policy => policy.AddAuthenticationSchemes(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme).RequireAuthenticatedUser()));
        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = 429;
            options.AddPolicy("open-api", new OpenApiRateLimitPolicy());
        });
        return settings;
    }
}
