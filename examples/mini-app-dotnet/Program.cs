using System.Net;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
var production = !builder.Environment.IsDevelopment();
var origin = new Uri(builder.Configuration["MiniApp:Origin"] ?? "http://127.0.0.1:5311");
var api = new Uri(builder.Configuration["ANKT:ApiUrl"] ?? "http://127.0.0.1:5000");
var hostOrigin = new Uri(builder.Configuration["ANKT:AppOrigin"] ?? "http://127.0.0.1:8081").GetLeftPart(UriPartial.Authority);
foreach (var uri in new[] { origin, api, new Uri(hostOrigin) })
    if (!string.IsNullOrEmpty(uri.UserInfo) || uri.Scheme != "https" && (production || uri.Scheme != "http" || !uri.IsLoopback)) throw new InvalidOperationException("HTTPS required outside loopback development");
var clientId = builder.Configuration["ANKT:ClientId"] ?? throw new InvalidOperationException("Configure ANKT__ClientId");
var clientSecret = builder.Configuration["ANKT:ClientSecret"];
var callback = origin.GetLeftPart(UriPartial.Authority) + "/auth/ankt/callback";
var cookie = production ? "__Host-mini-sso" : "mini-sso";
var secureCookies = origin.Scheme == "https";
var sameSite = secureCookies ? SameSiteMode.None : SameSiteMode.Lax;
builder.Services.AddDbContext<PartnerDb>(options => options.UseSqlite(builder.Configuration.GetConnectionString("Partner") ?? "Data Source=partner-mini-app.sqlite"));
builder.Services.AddScoped<PartnerIdentityService>();
builder.Services.AddSingleton<PasswordHasher<PartnerUser>>();
builder.Services.AddDataProtection().SetApplicationName("ANKT-partner-example");
builder.Services.AddAntiforgery(options => { options.Cookie.HttpOnly = true; options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest; options.Cookie.SameSite = sameSite; });
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(options =>
{
    options.Cookie.Name = production ? "__Host-mini-auth" : "mini-auth";
    options.Cookie.HttpOnly = true; options.Cookie.Path = "/"; options.Cookie.SameSite = sameSite;
    options.Cookie.SecurePolicy = secureCookies ? CookieSecurePolicy.Always : CookieSecurePolicy.SameAsRequest;
    options.ExpireTimeSpan = TimeSpan.FromHours(1); options.SlidingExpiration = false;
    options.Events.OnRedirectToLogin = context => { context.Response.StatusCode = 401; return Task.CompletedTask; };
});
builder.Services.AddAuthorization();
builder.Services.AddHttpClient("ANKT", options => { options.BaseAddress = api; options.Timeout = TimeSpan.FromSeconds(10); }).ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler { AllowAutoRedirect = false });
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = 429;
    options.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions { PermitLimit = 30, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
await using (var scope = app.Services.CreateAsyncScope()) await scope.ServiceProvider.GetRequiredService<PartnerDb>().Database.EnsureCreatedAsync();
app.UseExceptionHandler(handler => handler.Run(async context =>
{
    var error = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
    context.Response.StatusCode = error is AntiforgeryValidationException ? 400 : error is PartnerLinkConflict ? 409 : 500;
    await context.Response.WriteAsync(context.Response.StatusCode == 500 ? "Cannot complete this request" : error!.Message);
}));
app.Use(async (context, next) =>
{
    context.Response.Headers.CacheControl = "no-store"; context.Response.Headers["Referrer-Policy"] = "no-referrer"; context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    if (production) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    if (!string.Equals(context.Request.Host.Value, origin.Authority, StringComparison.OrdinalIgnoreCase)) { context.Response.StatusCode = 400; return; }
    // Do not log Request.QueryString; authorization codes occur only at this callback.
    await next();
});
app.UseAuthentication(); app.UseAuthorization(); app.UseRateLimiter();
static string Token() => WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
static string Hash(string value) => WebEncoders.Base64UrlEncode(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
static string E(string value) => WebUtility.HtmlEncode(value);
static Guid? UserId(HttpContext context) => Guid.TryParse(context.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
async Task SignIn(HttpContext context, Guid id)
{
    await context.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, id.ToString())], CookieAuthenticationDefaults.AuthenticationScheme)), new AuthenticationProperties { IsPersistent = false });
    context.Response.Cookies.Delete(cookie);
}
async Task<IFormCollection> Form(HttpContext context, IAntiforgery antiforgery)
{
    if (context.Request.Headers.Origin != origin.GetLeftPart(UriPartial.Authority)) throw new AntiforgeryValidationException("Invalid request origin");
    if (context.Request.ContentLength > 8192) throw new AntiforgeryValidationException("Request too large");
    await antiforgery.ValidateRequestAsync(context); return await context.Request.ReadFormAsync();
}
IResult Page(HttpContext context, string content, string? script = null)
{
    var nonce = Token();
    context.Response.Headers.ContentSecurityPolicy = $"default-src 'none'; script-src 'nonce-{nonce}' {api.GetLeftPart(UriPartial.Authority)}; style-src 'nonce-{nonce}'; base-uri 'none'; form-action 'self'; frame-ancestors {hostOrigin}";
    var scripts = script is null ? "" : $"<script nonce='{nonce}' src='{E(api.GetLeftPart(UriPartial.Authority))}/mini-app-sdk/ankt-mini-app.js'></script><script nonce='{nonce}'>{script}</script>";
    return Results.Content($"<!doctype html><html lang='vi'><meta charset='utf-8'><meta name='viewport' content='width=device-width,initial-scale=1'><title>ANKT partner</title><style nonce='{nonce}'>body{{font:16px system-ui;background:#f4f6f8;max-width:700px;margin:40px auto;padding:24px;color:#19232f}}main{{background:white;padding:28px;border-radius:16px;border:1px solid #dce2e8}}form,label{{display:grid;gap:10px;margin:20px 0}}input,button{{font:inherit;padding:12px;border:1px solid #becbd7;border-radius:8px}}button{{background:#145a73;color:white}}#error{{color:#a12323}}</style><main>{content}</main>{scripts}</html>", "text/html; charset=utf-8");
}
app.MapGet("/", async (HttpContext context, IAntiforgery antiforgery, PartnerDb db) =>
{
    var tokens = antiforgery.GetAndStoreTokens(context);
    var csrf = $"<input type='hidden' name='{E(tokens.FormFieldName)}' value='{E(tokens.RequestToken!)}'>";
    var id = UserId(context); var user = id is null ? null : await db.Users.FindAsync(id.Value);
    var fields = "<label>Tên đăng nhập<input name='username' required minlength='3' maxlength='64' autocomplete='username'></label><label>Mật khẩu<input name='password' type='password' required minlength='12' maxlength='256' autocomplete='current-password'></label>";
    var content = user is null ? $"<h1>Mini app đối tác</h1><form method='post' action='/login'>{csrf}{fields}<button>Đăng nhập tài khoản có sẵn</button></form><form method='post' action='/register'>{csrf}{fields}<button>Tạo tài khoản riêng</button></form><form method='post' action='/auth/ankt/start'>{csrf}<input type='hidden' name='intent' value='login'><button>Đăng nhập / đăng ký bằng ANKT</button></form>" : $"<h1>{E(user.DisplayName)}</h1><p>Phiên đăng nhập riêng của mini app.</p><form method='post' action='/auth/ankt/start'>{csrf}<input type='hidden' name='intent' value='link'><label>Xác nhận mật khẩu<input type='password' name='password' required autocomplete='current-password'></label><button>Liên kết ANKT</button></form><form method='post' action='/account/unlink'>{csrf}<label>Xác nhận mật khẩu<input type='password' name='password' required autocomplete='current-password'></label><button>Hủy liên kết ANKT</button></form><form method='post' action='/logout'>{csrf}<button>Đăng xuất mini app</button></form>";
    return Page(context, content);
});
foreach (var route in new[] { "/login", "/register" })
    app.MapPost(route, async (HttpContext context, IAntiforgery antiforgery, PartnerDb db, PasswordHasher<PartnerUser> hasher) =>
    {
        var form = await Form(context, antiforgery); var username = form["username"].ToString().Trim(); var password = form["password"].ToString();
        if (username.Length is < 3 or > 64 || password.Length is < 12 or > 256) return Results.BadRequest("Invalid username or password");
        var user = await db.Users.SingleOrDefaultAsync(user => user.Username == username);
        if (route == "/register")
        {
            if (user is not null) return Results.Conflict("Username unavailable");
            user = new PartnerUser { Username = username, DisplayName = username }; user.PasswordHash = hasher.HashPassword(user, password); db.Users.Add(user);
            try { await db.SaveChangesAsync(); } catch (DbUpdateException) { return Results.Conflict("Username unavailable"); }
        }
        else if (user?.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, password) == PasswordVerificationResult.Failed) return Results.Unauthorized();
        await SignIn(context, user.Id); return Results.Redirect("/");
    }).RequireRateLimiting("auth");
app.MapPost("/auth/ankt/start", async (HttpContext context, IAntiforgery antiforgery, PartnerDb db, PasswordHasher<PartnerUser> hasher, IDataProtectionProvider protection) =>
{
    var form = await Form(context, antiforgery); var intent = form["intent"].ToString(); var id = UserId(context);
    if (intent is not ("login" or "link")) return Results.BadRequest("Invalid intent");
    if (intent == "link")
    {
        var user = id is null ? null : await db.Users.FindAsync(id.Value);
        if (user?.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, form["password"].ToString()) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    }
    var browser = context.Request.Cookies[cookie];
    if (browser is null || browser.Length != 43) { browser = Token(); context.Response.Cookies.Append(cookie, browser, new CookieOptions { HttpOnly = true, Secure = secureCookies, SameSite = sameSite, Path = "/", MaxAge = TimeSpan.FromMinutes(5) }); }
    var state = Token(); var verifier = Token(); var now = DateTime.UtcNow;
    await db.Transactions.Where(transaction => transaction.ExpiresAt <= now || transaction.UsedAt != null).ExecuteDeleteAsync();
    db.Transactions.Add(new PartnerTransaction { StateHash = Hash(state), BrowserHash = Hash(browser), ProtectedVerifier = protection.CreateProtector("ANKT.PKCE").Protect(verifier), Intent = intent, UserId = intent == "link" ? id : null, ExpiresAt = now.AddMinutes(5) }); await db.SaveChangesAsync();
    var parameters = JsonSerializer.Serialize(new { redirectUri = callback, state, codeChallenge = Hash(verifier), codeChallengeMethod = "S256" });
    return Page(context, "<h1>Tiếp tục với ANKT</h1><button id='continue'>Tiếp tục</button><p id='error' role='alert'></p><a href='/'>Quay lại</a>", $"ANKT.configure({{hostOrigin:{JsonSerializer.Serialize(hostOrigin)}}});document.getElementById('continue').onclick=async()=>{{try{{const result=await ANKT.requestLogin({parameters});const target=new URL(result.launchUrl);if(target.origin!==location.origin||target.pathname!=='/auth/ankt/callback')throw new Error('Invalid callback');location.replace(target.href)}}catch(error){{document.getElementById('error').textContent=error.message}}}};");
}).RequireRateLimiting("auth");
app.MapGet("/auth/ankt/callback", async (HttpContext context, PartnerDb db, PartnerIdentityService identities, IHttpClientFactory clients, IDataProtectionProvider protection) =>
{
    var state = context.Request.Query["state"].ToString(); var code = context.Request.Query["code"].ToString(); var browser = context.Request.Cookies[cookie]; var now = DateTime.UtcNow;
    if (state.Length is < 1 or > 256 || code.Length is < 1 or > 256 || browser is null || browser.Length != 43) return Results.BadRequest("Invalid callback");
    var stateHash = Hash(state); var browserHash = Hash(browser);
    var transaction = await db.Transactions.AsNoTracking().SingleOrDefaultAsync(item => item.StateHash == stateHash && item.BrowserHash == browserHash && item.ExpiresAt > now && item.UsedAt == null);
    if (transaction is null || transaction.Intent == "link" && transaction.UserId != UserId(context)) return Results.BadRequest("SSO transaction does not belong to this session");
    var consumed = await db.Transactions.Where(item => item.StateHash == stateHash && item.BrowserHash == browserHash && item.UsedAt == null && item.ExpiresAt > now).ExecuteUpdateAsync(set => set.SetProperty(item => item.UsedAt, now));
    if (consumed != 1) return Results.BadRequest("SSO transaction already used");
    var verifier = protection.CreateProtector("ANKT.PKCE").Unprotect(transaction.ProtectedVerifier);
    using var response = await clients.CreateClient("ANKT").PostAsJsonAsync("/api/mini-app-auth/exchange", new { clientId, clientSecret, code, redirectUri = callback, state, codeVerifier = verifier }, context.RequestAborted);
    if (!response.IsSuccessStatusCode) return Results.Unauthorized();
    if (response.Content.Headers.ContentLength > 65536) return Results.BadRequest("Invalid identity response");
    var identity = await response.Content.ReadFromJsonAsync<AnktIdentity>(cancellationToken: context.RequestAborted);
    if (identity?.Subject is null || identity.Scopes is null || !identity.Scopes.Contains("identity.login", StringComparer.Ordinal)) return Results.BadRequest("identity.login permission required");
    var userId = await identities.ResolveAsync(identity.Subject, identity.DisplayName, transaction.Intent, transaction.UserId, context.RequestAborted);
    await SignIn(context, userId); return Results.Redirect("/");
}).RequireRateLimiting("auth");
app.MapPost("/account/unlink", async (HttpContext context, IAntiforgery antiforgery, PartnerDb db, PartnerIdentityService identities, PasswordHasher<PartnerUser> hasher) =>
{
    var form = await Form(context, antiforgery); var id = UserId(context); var user = id is null ? null : await db.Users.FindAsync(id.Value);
    if (user?.PasswordHash is null || hasher.VerifyHashedPassword(user, user.PasswordHash, form["password"].ToString()) == PasswordVerificationResult.Failed) return Results.Unauthorized();
    await identities.UnlinkAsync(user.Id, context.RequestAborted); await SignIn(context, user.Id); return Results.Redirect("/");
}).RequireAuthorization().RequireRateLimiting("auth");
app.MapPost("/logout", async (HttpContext context, IAntiforgery antiforgery, PartnerDb db) =>
{
    await Form(context, antiforgery); var browser = context.Request.Cookies[cookie];
    if (browser is not null) { var browserHash = Hash(browser); await db.Transactions.Where(transaction => transaction.BrowserHash == browserHash).ExecuteDeleteAsync(); }
    await context.SignOutAsync(); context.Response.Cookies.Delete(cookie); return Results.Redirect("/");
});
app.Run();
public sealed record AnktIdentity(string? Subject, string? DisplayName, IReadOnlyList<string>? Scopes);
