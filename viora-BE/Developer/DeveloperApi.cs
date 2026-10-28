using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;

namespace viora_BE.Developer;

public static class DeveloperApi
{
    public static IServiceCollection AddDeveloperAccounts(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<DeveloperAuthOptions>(config.GetSection("DeveloperAuth"));
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<IDeveloperIdentityService, DeveloperIdentityService>();
        services.AddScoped<SmtpDeveloperEmailSender>();
        services.AddHttpClient<GmailDeveloperEmailSender>(client => client.Timeout = TimeSpan.FromSeconds(15))
            .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
        services.AddScoped<IDeveloperEmailSender>(provider =>
            provider.GetRequiredService<Microsoft.Extensions.Options.IOptions<DeveloperAuthOptions>>().Value.EmailProvider switch
            {
                "GmailApi" => provider.GetRequiredService<GmailDeveloperEmailSender>(),
                "Smtp" => provider.GetRequiredService<SmtpDeveloperEmailSender>(),
                _ => throw new InvalidOperationException("Unsupported Developer email provider. Use Smtp or GmailApi.")
            });
        services.AddAuthentication().AddScheme<AuthenticationSchemeOptions, DeveloperSessionHandler>(DeveloperSessionHandler.SchemeName, _ => { });
        services.AddAuthorization(o => o.AddPolicy(DeveloperSessionHandler.Policy, p => p
            .AddAuthenticationSchemes(DeveloperSessionHandler.SchemeName).RequireAuthenticatedUser().RequireClaim("developer_status", "Active")));
        services.AddRateLimiter(o =>
        {
            o.RejectionStatusCode = 429;
            o.AddPolicy("developer-auth", context =>
            {
                var authenticated = context.User.Identity?.AuthenticationType == DeveloperSessionHandler.SchemeName &&
                    context.GetEndpoint()?.Metadata.GetMetadata<Microsoft.AspNetCore.Authorization.IAllowAnonymous>() is null;
                var partition = authenticated ? "account:" + context.User.FindFirstValue("developer_id") :
                    "ip:" + DeveloperIngress.ClientIp(context, config["DeveloperAuth:BffSharedSecret"] ?? "");
                return RateLimitPartition.GetFixedWindowLimiter(partition, _ => new FixedWindowRateLimiterOptions
                { PermitLimit = authenticated ? 180 : 60, Window = TimeSpan.FromMinutes(1), QueueLimit = 0, AutoReplenishment = true });
            });
        });
        return services;
    }

    public static WebApplication UseDeveloperAccountBoundary(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api/developer")) { await next(context); return; }
            context.Response.Headers.CacheControl = "no-store";
            if (context.Request.Headers.ContainsKey("X-ANKT-Bff-IP") && !DeveloperIngress.TryClientIp(context,
                context.RequestServices.GetRequiredService<IConfiguration>()["DeveloperAuth:BffSharedSecret"] ?? "", out _))
            {
                context.Response.StatusCode = 503;
                await context.Response.WriteAsJsonAsync(new { error = new { code = "INGRESS_UNAVAILABLE", message = "Cấu hình kết nối portal chưa sẵn sàng." } });
                return;
            }
            if (context.Request.Headers.Authorization.ToString().StartsWith(DeveloperSessionHandler.SchemeName + " ", StringComparison.Ordinal))
            {
                var authentication = await context.AuthenticateAsync(DeveloperSessionHandler.SchemeName);
                if (authentication.Succeeded) context.User = authentication.Principal!;
            }
            var maxBytes = context.Request.Path == "/api/developer/mini-apps/logo" ? MiniAppLogoStorage.MaxBytes : 65536;
            if (!context.Request.Path.StartsWithSegments("/api/developer/mini-apps")) maxBytes = 16384;
            var limit = context.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (limit is { IsReadOnly: false }) limit.MaxRequestBodySize = maxBytes;
            try
            {
                if (context.Request.ContentLength > maxBytes) throw new BadHttpRequestException("Request too large", 413);
                await next(context);
            }
            catch (BadHttpRequestException ex) when (!context.Response.HasStarted)
            {
                context.Response.StatusCode = ex.StatusCode;
                await context.Response.WriteAsJsonAsync(new { error = new { code = "INVALID_REQUEST", message = "Dữ liệu gửi không hợp lệ hoặc quá lớn." } });
            }
        });
        return app;
    }

    public static IEndpointRouteBuilder MapDeveloperAccountEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/developer").WithTags("Developer Accounts")
            .AddEndpointFilter<DeveloperApiFilter>().RequireRateLimiting("developer-auth");
        group.MapPost("/auth/register", async (DeveloperRegisterRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
        { await service.RegisterAsync(request, Context(http), ct); return NeutralEmail(); }).AllowAnonymous();
        group.MapPost("/auth/login", async (DeveloperLoginRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
            Results.Ok(await service.LoginAsync(request, Context(http), ct))).AllowAnonymous();
        group.MapPost("/auth/verify-email", async (DeveloperTokenRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
        { await service.VerifyEmailAsync(request.Token, Context(http), ct); return Results.Ok(new { message = "Email đã được xác minh. Hãy đăng nhập để tiếp tục." }); }).AllowAnonymous();
        group.MapPost("/auth/resend-verification", async (DeveloperEmailRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
        { await service.SendEmailActionAsync(request.Email, false, Context(http), ct); return NeutralEmail(); }).AllowAnonymous();
        group.MapPost("/auth/forgot-password", async (DeveloperEmailRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
        { await service.SendEmailActionAsync(request.Email, true, Context(http), ct); return NeutralEmail(); }).AllowAnonymous();
        group.MapPost("/auth/reset-password", async (DeveloperResetRequest request, IDeveloperIdentityService service, HttpContext http, CancellationToken ct) =>
        { await service.ResetPasswordAsync(request, Context(http), ct); return Results.Ok(new { message = "Đã đổi mật khẩu và đăng xuất các phiên cũ." }); }).AllowAnonymous();

        var active = group.MapGroup("").RequireAuthorization(DeveloperSessionHandler.Policy);
        active.MapGet("/auth/me", GetMe);
        active.MapGet("/profile", GetMe);
        active.MapPost("/auth/refresh", async (IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.RefreshAsync(Session(h), ct)));
        active.MapPost("/auth/logout", async (IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => { await s.LogoutAsync(Session(h), false, Context(h), ct); return Results.NoContent(); });
        active.MapPost("/auth/logout-all", async (IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => { await s.LogoutAsync(Session(h), true, Context(h), ct); return Results.NoContent(); });
        active.MapPost("/auth/change-password", async (DeveloperChangePasswordRequest r, IDeveloperIdentityService s, HttpContext h, CancellationToken ct) =>
        { await s.ChangePasswordAsync(Account(h), r, Context(h), ct); return Results.Ok(new { message = "Đã đổi mật khẩu. Vui lòng đăng nhập lại." }); });
        active.MapPatch("/profile", async (DeveloperProfileRequest r, IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.UpdateProfileAsync(Account(h), r, Context(h), ct)));
        active.MapGet("/sessions", async (IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.SessionsAsync(Account(h), Session(h), ct)));
        active.MapDelete("/sessions/{id:guid}", async (Guid id, IDeveloperIdentityService s, HttpContext h, CancellationToken ct) =>
        { await s.RevokeSessionAsync(Account(h), id, Context(h), ct); return Results.NoContent(); });
        active.MapGet("/linked-accounts", async (IDeveloperIdentityService s, HttpContext h, CancellationToken ct) => Results.Ok(await s.LinkedAccountsAsync(Account(h), ct)));
        active.MapDelete("/linked-accounts/{id:guid}", async (Guid id, [Microsoft.AspNetCore.Mvc.FromBody] DeveloperUnlinkRequest r, IDeveloperIdentityService s, HttpContext h, CancellationToken ct) =>
        { await s.UnlinkAsync(Account(h), id, r.CurrentPassword, Context(h), ct); return Results.NoContent(); });
        // An ANKT JWT is not an OAuth/OIDC authorization grant. Never accept it here.
        group.MapPost("/linked-accounts/ankt/start", () => FederationUnavailable()).AllowAnonymous();
        group.MapGet("/linked-accounts/ankt/callback", () => FederationUnavailable()).AllowAnonymous();
        return endpoints;
    }

    private static async Task<IResult> GetMe(IDeveloperIdentityService service, HttpContext http, CancellationToken ct)
    {
        var header = http.Request.Headers.Authorization.ToString();
        var session = await service.AuthenticateAsync(header[(DeveloperSessionHandler.SchemeName.Length + 1)..], ct);
        return session is null ? Results.Json(new { error = new { code = "UNAUTHENTICATED", message = "Phiên đã hết hạn. Vui lòng đăng nhập lại." } }, statusCode: 401) : Results.Ok(session.Account);
    }
    private static Guid Account(HttpContext h) => Guid.Parse(h.User.FindFirstValue("developer_id")!);
    private static Guid Session(HttpContext h) => Guid.Parse(h.User.FindFirstValue("developer_session")!);
    private static DeveloperRequestContext Context(HttpContext h) => new(DeveloperIngress.ClientIp(h,
        h.RequestServices.GetRequiredService<IConfiguration>()["DeveloperAuth:BffSharedSecret"] ?? ""), h.Request.Headers.UserAgent.ToString());
    private static IResult NeutralEmail() => Results.Json(new { message = "Nếu thông tin phù hợp, bạn sẽ nhận được email hướng dẫn. Kiểm tra hộp thư và thư rác; có thể gửi lại sau 60 giây." }, statusCode: 202);
    private static IResult FederationUnavailable() => Results.Json(new { error = new { code = "ANKT_FEDERATION_UNAVAILABLE", message = "ANKT chưa cung cấp OAuth/OIDC cho Developer. Vui lòng sử dụng tài khoản Developer độc lập." } }, statusCode: 501);
}

public sealed class DeveloperApiFilter : IEndpointFilter
{
    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        context.HttpContext.Response.Headers.CacheControl = "no-store";
        try
        {
            foreach (var arg in context.Arguments.Where(a => a?.GetType().Namespace == typeof(DeveloperRegisterRequest).Namespace))
            {
                var errors = new List<ValidationResult>();
                if (arg is not null && !Validator.TryValidateObject(arg, new ValidationContext(arg), errors, true))
                    return Results.Json(new { error = new { code = "VALIDATION_ERROR", message = "Thông tin không hợp lệ.", details = errors.Select(e => new { fields = e.MemberNames, message = e.ErrorMessage }) } }, statusCode: 400);
            }
            return await next(context);
        }
        catch (DeveloperAuthException ex) { return Results.Json(new { error = new { code = ex.Code, message = ex.Message } }, statusCode: ex.Status); }
        catch (DbUpdateConcurrencyException) { return Results.Json(new { error = new { code = "RETRY_REQUIRED", message = "Dữ liệu vừa thay đổi. Vui lòng thử lại." } }, statusCode: 409); }
    }
}
