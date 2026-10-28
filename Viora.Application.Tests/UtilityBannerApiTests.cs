using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Viora.Application.Utilities;
using Viora.Infrastructure.Persistence;
using Viora.Infrastructure.Utilities;
using viora_BE.Controllers;
using Xunit;

public sealed class UtilityBannerApiTests
{
    [Fact]
    public async Task RoleBoundaryAndRealHttpCrud()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:"); await connection.OpenAsync();
        connection.CreateFunction<string?, int>("char_length", value => value?.Length ?? 0);
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
        builder.Services.AddScoped<IUtilityBannerRepository, UtilityBannerRepository>();
        builder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, Auth>("fixture", _ => { });
        builder.Services.AddAuthorization();
        builder.Services.AddControllers().AddApplicationPart(typeof(UtilityBannersController).Assembly);
        await using var app = builder.Build(); app.UseAuthentication(); app.UseAuthorization(); app.MapControllers();
        using (var scope = app.Services.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.EnsureCreatedAsync();
        await app.StartAsync(); var client = app.GetTestClient();
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/utility-banners")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/admin/utility-banners")).StatusCode);
        client.DefaultRequestHeaders.Add("X-Fixture-Role", "0");
        Assert.Equal(HttpStatusCode.Forbidden, (await client.PostAsJsonAsync("/api/admin/utility-banners", Input())).StatusCode);
        client.DefaultRequestHeaders.Remove("X-Fixture-Role"); client.DefaultRequestHeaders.Add("X-Fixture-Role", "2");
        var create = await client.PostAsJsonAsync("/api/admin/utility-banners", Input());
        Assert.Equal(HttpStatusCode.OK, create.StatusCode);
        var item = (await create.Content.ReadFromJsonAsync<UtilityBannerItem>())!;
        var list = await client.GetFromJsonAsync<UtilityBannerItem[]>("/api/admin/utility-banners"); Assert.Single(list!);
        Assert.Single((await client.GetFromJsonAsync<object[]>("/api/utility-banners"))!);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/admin/utility-banners", Input() with { ImageUrl = "javascript:alert(1)" })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await client.PutAsJsonAsync($"/api/admin/utility-banners/{item.Id}", Input() with { IsActive = false, Version = item.Version })).StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<object[]>("/api/utility-banners"))!);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PutAsJsonAsync($"/api/admin/utility-banners/{item.Id}", Input() with { Version = item.Version })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.DeleteAsync($"/api/admin/utility-banners/{item.Id}?version={item.Version}")).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await client.DeleteAsync($"/api/admin/utility-banners/{item.Id}?version={item.Version + 1}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.DeleteAsync($"/api/admin/utility-banners/{item.Id}?version={item.Version + 1}")).StatusCode);
    }

    private static SaveUtilityBanner Input() => new("Sample", "https://images.example.test/banner.png", null, 0, true, 0);
    private sealed class Auth(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var role = Request.Headers["X-Fixture-Role"].ToString();
            return Task.FromResult(string.IsNullOrEmpty(role) ? AuthenticateResult.NoResult() :
                AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, role) }, Scheme.Name)), Scheme.Name)));
        }
    }
}
