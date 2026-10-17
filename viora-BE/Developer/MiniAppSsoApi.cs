using System.Security.Claims;
using Microsoft.Extensions.Options;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using Viora.Application.Developers;
using Viora.Infrastructure.Developers;
using viora_BE.Sso;

namespace viora_BE.Developer;

public static class MiniAppSsoApi
{
    public static void MapEndpoints(RouteGroupBuilder group)
    {
        group.MapGet("/{appId}/sso",async(string appId,MiniAppSsoService service,HttpContext http,CancellationToken ct)=>
            Results.Ok((await service.Get(Owner(http),appId,ct)) with {Server=Server(http.RequestServices)}));
        group.MapPut("/{appId}/sso/{environment}",async(string appId,string environment,MiniAppSsoRequest request,MiniAppSsoService service,HttpContext http,CancellationToken ct)=>
            Results.Ok((await service.Save(Owner(http),appId,environment,request,ct)) with {Server=Server(http.RequestServices)})).RequireRateLimiting("mini-app-sensitive");
        // Provision only to the authenticated owner's backend tooling. BFF does not allow this route.
        group.MapGet("/{appId}/sso/{environment}/credential",async Task<IResult>(string appId,string environment,MiniAppSsoService service,HttpContext http,CancellationToken ct)=>
        {
            if(http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Sec-Fetch-Site"))
                return Results.Json(new {error=new {code="SSO_BACKEND_REQUIRED",message="Chỉ cấp credential cho công cụ backend đã xác thực."}},statusCode:403);
            return Results.Ok(new {clientSecret=await service.Credential(Owner(http),appId,environment,ct)});
        }).RequireRateLimiting("mini-app-sensitive");
        group.MapPost("/{appId}/sso/{environment}/credential/rotate",async Task<IResult>(string appId,string environment,MiniAppSsoCredentialRotationRequest request,MiniAppSsoService service,HttpContext http,CancellationToken ct)=>
        {
            if(http.Request.Headers.ContainsKey("Origin") || http.Request.Headers.ContainsKey("Sec-Fetch-Site"))
                return Results.Json(new {error=new {code="SSO_BACKEND_REQUIRED",message="Chỉ cấp credential cho công cụ backend đã xác thực."}},statusCode:403);
            return Results.Ok(await service.RotateCredential(Owner(http),appId,environment,request.ClientVersion,ct));
        }).RequireRateLimiting("mini-app-sensitive");
    }
    private static Guid Owner(HttpContext http)=>Guid.Parse(http.User.FindFirstValue("developer_id")!);
    private static MiniAppSsoServerDto Server(IServiceProvider services)
    {
        var settings=services.GetService<IOptions<SsoOptions>>()?.Value;
        if(settings?.Enabled!=true) return new(false,null,null,null,null,null,null,false);
        var actual=services.GetRequiredService<IOptionsMonitor<OpenIddictServerOptions>>().CurrentValue;
        var issuer=actual.Issuer ?? throw new InvalidOperationException("Missing validated SSO issuer.");
        string? Endpoint(IEnumerable<Uri> values)=>values.FirstOrDefault() is {} value?new Uri(issuer,value).AbsoluteUri:null;
        return new(true,issuer.AbsoluteUri,Endpoint(actual.ConfigurationEndpointUris),Endpoint(actual.AuthorizationEndpointUris),
            Endpoint(actual.TokenEndpointUris),Endpoint(actual.UserInfoEndpointUris),Endpoint(actual.JsonWebKeySetEndpointUris),actual.GrantTypes.Contains(OpenIddictConstants.GrantTypes.RefreshToken));
    }
}
