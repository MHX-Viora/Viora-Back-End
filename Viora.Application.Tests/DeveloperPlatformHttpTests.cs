using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.MiniApps;
using Viora.Infrastructure.MiniApps;
using Xunit;
using System.Threading.RateLimiting;

public sealed class DeveloperPlatformHttpTests
{
    private sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> o,ILoggerFactory log,UrlEncoder encoder):AuthenticationHandler<AuthenticationSchemeOptions>(o,log,encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if(!Guid.TryParse(Request.Headers["Fixture-Account"],out var id))return Task.FromResult(AuthenticateResult.NoResult());
            var claims=new[]{new Claim("sub",id.ToString()),new Claim(ClaimTypes.Role,Request.Headers["Fixture-Role"].ToString())};
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(claims,Scheme.Name)),Scheme.Name)));
        }
    }
    private sealed class UnconfiguredMailer:IDeveloperVerificationMailer { public Task SendCodeAsync(string email,string code,CancellationToken ct)=>throw new MiniAppException("EMAIL_DELIVERY_UNAVAILABLE","Not configured.",503); }
    [Fact]
    public async Task RoutesEnforceAuthenticationAdminRoleAndAllowSubmissionWithoutEmailDelivery()
    {
        await using var f=await MiniAppHybridIntegrationTests.Fixture.Create(true);
        var builder=WebApplication.CreateBuilder();builder.WebHost.UseTestServer();
        builder.Services.AddSingleton(f.Db);builder.Services.AddSingleton<IDeveloperMiniAppService>(f.Management);builder.Services.AddSingleton<IMiniAppManagementService>(f.Management);
        builder.Services.AddSingleton<Viora.Application.Accounts.IPasswordHasher,Viora.Infrastructure.Security.AspNetIdentityPasswordHasher>();
        builder.Services.AddSingleton<IDeveloperReauthenticationService,DeveloperReauthenticationService>();
        builder.Services.AddSingleton<TimeProvider>(TimeProvider.System);builder.Services.Configure<DeveloperVerificationOptions>(o=>o.OtpHashKey=new string('z',32));builder.Services.AddSingleton<IDeveloperVerificationMailer,UnconfiguredMailer>();builder.Services.AddScoped<IDeveloperPlatformService,DeveloperPlatformService>();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions,FixtureAuthentication>("fixture",_=>{});builder.Services.AddAuthorization();
        builder.Services.AddRateLimiter(o=>o.AddFixedWindowLimiter("mini-app-launch",l=>{l.PermitLimit=100;l.Window=TimeSpan.FromMinutes(1);}));
        builder.Services.AddControllers().AddApplicationPart(typeof(viora_BE.Controllers.Developer.DeveloperProfileController).Assembly);
        await using var app=builder.Build();app.Use(async(context,next)=>{try{await next(context);}catch(MiniAppException e){context.Response.StatusCode=e.StatusCode;await context.Response.WriteAsJsonAsync(new{code=e.Code,message=e.Message});}});
        app.UseAuthentication();app.UseAuthorization();app.UseRateLimiter();app.MapControllers();await app.StartAsync();var http=app.GetTestClient();
        Assert.Equal(HttpStatusCode.Unauthorized,(await http.GetAsync("/api/developer/profile")).StatusCode);
        http.DefaultRequestHeaders.Add("Fixture-Account",f.Other.Id.ToString());http.DefaultRequestHeaders.Add("Fixture-Role","0");
        Assert.Equal(HttpStatusCode.Forbidden,(await http.GetAsync($"/api/admin/developers/{f.Db.Developers.First().Id}/application")).StatusCode);
        var response=await http.PostAsJsonAsync("/api/developer/profile",new DeveloperInput("New partner",null,"new@example.com","0901234567",null,f.Owner.Id,Country:"VN"));response.EnsureSuccessStatusCode();var draft=await response.Content.ReadFromJsonAsync<DeveloperProfileDto>();
        Assert.Equal(f.Other.Id,draft!.AccountId);Assert.Equal("Draft",draft.RegistrationStatus);
        var submitted=await http.PostAsJsonAsync("/api/developer/application/submit",new DeveloperSubmitInput(draft.RegistrationVersion,DeveloperPlatformService.TermsVersion,true));
        submitted.EnsureSuccessStatusCode();
        Assert.Equal("PendingReview",(await submitted.Content.ReadFromJsonAsync<DeveloperProfileDto>())!.RegistrationStatus);
        Assert.Equal(HttpStatusCode.ServiceUnavailable,(await http.PostAsJsonAsync("/api/developer/email-verification/request",new DeveloperEmailRequest(draft.Email))).StatusCode);
    }
}
