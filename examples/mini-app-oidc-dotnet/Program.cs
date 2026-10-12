using System.Net.Http.Headers;
using System.Security.Claims;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;

var builder = WebApplication.CreateBuilder(args);
var issuer = new Uri(builder.Configuration["ANKT:Issuer"] ?? throw new InvalidOperationException("Configure ANKT__Issuer"));
if (issuer.Scheme != "https" || issuer.AbsolutePath != "/" || issuer.UserInfo.Length > 0 || issuer.Query.Length > 0 || issuer.Fragment.Length > 0) throw new InvalidOperationException("Exact HTTPS issuer origin required.");
var clientId = builder.Configuration["ANKT:ClientId"] ?? throw new InvalidOperationException("Configure ANKT__ClientId");
builder.Services.AddDbContext<PartnerIdentityDb>(options => options.UseSqlite(builder.Configuration.GetConnectionString("Partner") ?? "Data Source=partner-oidc.sqlite"));
builder.Services.AddSingleton<ServerTickets>();
builder.Services.AddHttpClient("issuer", client => { client.BaseAddress = issuer; client.Timeout = TimeSpan.FromSeconds(10); }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddAntiforgery(options => { options.HeaderName = "X-CSRF-Token"; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.None; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options => {
    options.Cookie.Name = "__Host-partner-session"; options.Cookie.Path = "/"; options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = CookieSecurePolicy.Always; options.Cookie.SameSite = SameSiteMode.None;
    options.ExpireTimeSpan = TimeSpan.FromMinutes(30); options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
    options.Events.OnValidatePrincipal = async context => {
        var token = context.Properties.GetTokenValue("access_token");
        if (token is null) { context.RejectPrincipal(); return; }
        using var request = new HttpRequestMessage(HttpMethod.Get, "v1/me"); request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        try { using var response = await context.HttpContext.RequestServices.GetRequiredService<IHttpClientFactory>().CreateClient("issuer").SendAsync(request); if (!response.IsSuccessStatusCode) { context.RejectPrincipal(); await context.HttpContext.SignOutAsync(); } }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException) { context.RejectPrincipal(); }
    };
}).AddOpenIdConnect("ankt", options => {
    options.Authority = issuer.AbsoluteUri; options.ClientId = clientId; options.ClientSecret = builder.Configuration["ANKT:ClientSecret"];
    options.CallbackPath = "/auth/ankt/callback"; options.ResponseType = OpenIdConnectResponseType.Code; options.ResponseMode = OpenIdConnectResponseMode.Query;
    options.UsePkce = true; options.MapInboundClaims = false; options.SaveTokens = true;
    options.Scope.Clear(); options.Scope.Add("openid"); options.Scope.Add("profile"); options.TokenValidationParameters.ValidAudience = clientId;
    options.Events.OnTokenValidated = async context => {
        if (context.Properties!.Items.TryGetValue("ankt_runtime", out var expectedRuntime) && context.Principal?.FindFirstValue("runtime_session_id") != expectedRuntime)
            throw new InvalidOperationException("Runtime binding mismatch.");
        var subject = context.Principal?.FindFirstValue("sub") ?? throw new InvalidOperationException("Verified subject required.");
        var db = context.HttpContext.RequestServices.GetRequiredService<PartnerIdentityDb>();
        var identity = await db.Identities.FindAsync(issuer.AbsoluteUri, subject);
        if (identity is null) {
            identity = new PartnerIdentity { Issuer = issuer.AbsoluteUri, Subject = subject }; db.Identities.Add(identity);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { db.Entry(identity).State = EntityState.Detached; identity = await db.Identities.FindAsync(issuer.AbsoluteUri, subject); if (identity is null) throw; }
        }
        ((ClaimsIdentity)context.Principal!.Identity!).AddClaim(new Claim("partner_user_id", identity.UserId.ToString()));
    };
    options.Events.OnRemoteFailure = context => { context.HandleResponse(); context.Response.StatusCode = 400; return context.Response.WriteAsJsonAsync(new { error = "OIDC_CALLBACK_FAILED" }); };
});
builder.Services.AddOptions<CookieAuthenticationOptions>(CookieAuthenticationDefaults.AuthenticationScheme).Configure<ServerTickets>((options, store) => options.SessionStore = store);
builder.Services.AddAuthorization();
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<PartnerIdentityDb>().Database.EnsureCreatedAsync();
app.UseHttpsRedirection(); app.UseAuthentication(); app.UseAuthorization();
app.Use(async (context, next) => { context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; await next(); });
app.MapGet("/auth/ankt/session", (HttpContext context, IAntiforgery csrf) => Results.Json(new { authenticated = context.User.Identity?.IsAuthenticated == true, runtimeSessionId = context.User.FindFirstValue("runtime_session_id"), csrfToken = csrf.GetAndStoreTokens(context).RequestToken, issuer = issuer.GetLeftPart(UriPartial.Authority) }));
app.MapPost("/auth/ankt/start", async (HttpContext context, IAntiforgery csrf) => {
    try { await csrf.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { context.Response.StatusCode = 400; return; }
    var input = await context.Request.ReadFromJsonAsync<StartInput>();
    var properties = new AuthenticationProperties { RedirectUri = "/" };
    if (input?.RuntimeSessionId is not null) {
        if (!Guid.TryParse(input.RuntimeSessionId, out _)) { context.Response.StatusCode = 400; return; }
        properties.Items["ankt_runtime"] = input.RuntimeSessionId;
    }
    await context.ChallengeAsync("ankt", properties);
    var authorize = context.Response.Headers.Location.ToString(); var url = new Uri(authorize); var query = QueryHelpers.ParseQuery(url.Query);
    context.Response.Headers.Remove("Location"); context.Response.StatusCode = 200;
    await context.Response.WriteAsJsonAsync(new { authorizeUrl = authorize, sdkParams = new { clientId, redirectUri = query["redirect_uri"].ToString(), state = query["state"].ToString(), nonce = query["nonce"].ToString(), codeChallenge = query["code_challenge"].ToString(), codeChallengeMethod = "S256", scopes = query["scope"].ToString().Split(' ') } });
});
app.MapGet("/auth/ankt/profile", (ClaimsPrincipal user) => Results.Json(new { userId = user.FindFirstValue("partner_user_id"), sub = user.FindFirstValue("sub"), name = user.FindFirstValue("name"), picture = user.FindFirstValue("picture") })).RequireAuthorization();
app.MapPost("/auth/ankt/logout", async (HttpContext context, IAntiforgery csrf) => { try { await csrf.ValidateRequestAsync(context); } catch (AntiforgeryValidationException) { return Results.BadRequest(); } await context.SignOutAsync(); return Results.Json(new { loggedOut = true }); });
app.MapGet("/", () => Results.Content("<html lang='vi'><meta name='viewport' content='width=device-width'><h1>Website đối tác</h1><p>Nạp React/SDK frontend và dùng BFF /auth/ankt/* cùng origin.</p></html>", "text/html"));
app.Run();
record StartInput(string? RuntimeSessionId);
