using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.MiniApps;
using Viora.Domain.Entities;
using Viora.Infrastructure.MiniApps;
using Viora.Infrastructure.Security;
using Xunit;
using Fixture = global::MiniAppHybridIntegrationTests.Fixture;

public sealed class DeveloperMembershipHttpTests
{
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory log, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, log, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Guid.TryParse(Request.Headers["Fixture-Account"], out var id)
            ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", id.ToString())], Scheme.Name)), Scheme.Name))
            : AuthenticateResult.NoResult());
    }
    [Fact]
    public async Task ContextAndAccountBoundInvitationWorkThroughAuthenticatedRoutes()
    {
        await using var f = await Fixture.Create(true);
        var organization = new Developer { Name = "Second", Email = "second-http@example.com", Status = DeveloperStatus.Active };
        f.Db.Developers.Add(organization); f.Db.DeveloperMemberships.Add(new() { DeveloperId = organization.Id, AccountId = f.Owner.Id, Role = DeveloperMembershipRole.Developer }); await f.Db.SaveChangesAsync();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Services.AddHttpContextAccessor(); builder.Services.AddScoped<IDeveloperOrganizationContext, viora_BE.Services.DeveloperOrganizationContext>();
        builder.Services.AddScoped<MiniAppManagementService>(sp => new(f.Db, new ClientCredentialService(), organizationContext: sp.GetRequiredService<IDeveloperOrganizationContext>()));
        builder.Services.AddScoped<IDeveloperMembershipService>(sp => sp.GetRequiredService<MiniAppManagementService>());
        builder.Services.AddScoped<IDeveloperMiniAppService>(sp => sp.GetRequiredService<MiniAppManagementService>());
        builder.Services.AddScoped<IMiniAppManagementService>(sp => sp.GetRequiredService<MiniAppManagementService>());
        builder.Services.AddSingleton<IDeveloperReauthenticationService>(new DeveloperReauthenticationService(f.Db, new AspNetIdentityPasswordHasher()));
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, Auth>("fixture", _ => { }); builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(viora_BE.Controllers.Developer.DeveloperMembershipController).Assembly);
        await using var app = builder.Build();
        app.Use(async (context, next) => { try { await next(context); } catch (MiniAppException e) { context.Response.StatusCode = e.StatusCode; await context.Response.WriteAsJsonAsync(new { code = e.Code }); } });
        app.UseAuthentication(); app.UseAuthorization(); app.MapControllers(); await app.StartAsync(); var http = app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await http.GetAsync("/api/developer/organizations")).StatusCode);
        http.DefaultRequestHeaders.Add("Fixture-Account", f.Owner.Id.ToString()); http.DefaultRequestHeaders.Add("X-Developer-Organization", organization.Id.ToString());
        var createdResponse = await http.PostAsJsonAsync("/api/developer/mini-apps", f.Input() with { Slug = "http-selected" }); createdResponse.EnsureSuccessStatusCode();
        var created = (await createdResponse.Content.ReadFromJsonAsync<MiniAppCredentialResponse>())!;
        Assert.Equal(organization.Id, f.Db.MiniApps.Single(a => a.Id == created.Id).DeveloperId);
        http.DefaultRequestHeaders.Remove("X-Developer-Organization"); http.DefaultRequestHeaders.Add("X-Developer-Organization", "invalid");
        Assert.Equal(HttpStatusCode.UnprocessableEntity, (await http.PostAsJsonAsync("/api/developer/mini-apps", f.Input())).StatusCode);
        http.DefaultRequestHeaders.Remove("X-Developer-Organization");
        var invitationResponse = await http.PostAsJsonAsync($"/api/developer/mini-apps/{created.Id}/invitations", new MiniAppMemberInvitationInput(f.Other.Id, "Viewer")); invitationResponse.EnsureSuccessStatusCode();
        var invitation = (await invitationResponse.Content.ReadFromJsonAsync<MiniAppMemberInvitationDto>())!;
        Assert.Equal(HttpStatusCode.NotFound, (await http.PostAsync($"/api/developer/invitations/{invitation.Id}/accept", null)).StatusCode);
        http.DefaultRequestHeaders.Remove("Fixture-Account"); http.DefaultRequestHeaders.Add("Fixture-Account", f.Other.Id.ToString());
        (await http.PostAsync($"/api/developer/invitations/{invitation.Id}/accept", null)).EnsureSuccessStatusCode();
        (await http.GetAsync($"/api/developer/mini-apps/{created.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Forbidden, (await http.PutAsJsonAsync($"/api/developer/mini-apps/{created.Id}", f.Input())).StatusCode);
    }
}
