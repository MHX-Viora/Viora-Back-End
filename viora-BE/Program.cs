using Viora.Infrastructure;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using System.Text.Json;
using viora_BE.OpenApi;
using System.Threading.RateLimiting;
using Viora.Application.Posts;
using Viora.Infrastructure.Realtime;

Environment.SetEnvironmentVariable("DOTNET_HOSTBUILDER__RELOADCONFIGONCHANGE", "false");

var builder = WebApplication.CreateBuilder(args);
var port = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrWhiteSpace(port))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{port}");
}
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Add services to the container.

builder.Services.AddControllers();
builder.Services.AddMediatR(configuration =>
    configuration.RegisterServicesFromAssembly(typeof(GetCommunityPostsQuery).Assembly));
var jwtKey = builder.Configuration["Jwt:Key"] ?? string.Empty;
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "viora-BE";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "viora-client";
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateLifetime = true,
            RequireExpirationTime = true,
            RequireSignedTokens = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub",
            RoleClaimType = "role"
        };
        options.Events = new JwtBearerEvents
        {
            OnTokenValidated = context =>
            {
                if (context.Principal?.FindFirst("token_type")?.Value != "access")
                {
                    context.Fail("Invalid token type.");
                }
                return Task.CompletedTask;
            },
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
if (!string.IsNullOrWhiteSpace(accessToken) &&
                    (path.StartsWithSegments("/hubs/realtime") || path.StartsWithSegments("/hubs/calls")))
                {
                    context.Token = accessToken;
                }

                return Task.CompletedTask;
            },
            OnChallenge = async context =>
            {
                context.HandleResponse();
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/json";
                await context.Response.WriteAsync(JsonSerializer.Serialize(new
                {
                    message = "Bạn chưa đăng nhập hoặc token không hợp lệ."
                }));
            }
        };
    });
builder.Services.AddRateLimiter(options => options.AddPolicy("auth", context =>
    RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 5,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        })));
builder.Services.AddSignalR();
builder.Services.AddInfrastructure(builder.Configuration);
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header
    });
    options.OperationFilter<AuthorizeOperationFilter>();
});


builder.Services.AddCors(options =>
{
    options.AddPolicy("Web", policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:5173",
                "http://localhost:3000",
                "https://vioraadmin.vercel.app"
            )
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

var app = builder.Build();
app.Logger.LogInformation("Starting Viora API on port {Port}", port ?? "launchSettings/default");

app.UseExceptionHandler(errorApp =>
{
    errorApp.Run(async context =>
    {
        var exception = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>()?.Error;
        var traceId = context.TraceIdentifier;

        app.Logger.LogError(
            exception,
            "Unhandled request exception. TraceId: {TraceId}, Path: {Path}, Method: {Method}.",
            traceId,
            context.Request.Path,
            context.Request.Method);

        if (context.Response.HasStarted)
        {
            return;
        }

        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";
        await context.Response.WriteAsJsonAsync(new
        {
            success = false,
            message = "Internal server error. Check server logs with traceId.",
            traceId
        });
    });
});

app.UseHttpsRedirection();

app.UseStaticFiles();
app.UseCors("Web");
app.UseSwagger();
app.UseSwaggerUI(options =>
{
	options.RoutePrefix = string.Empty;
	options.SwaggerEndpoint(
		"/swagger/v1/swagger.json",
		"Viora API v1"
	);
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
	status = "healthy",
	service = "viora-back-end",
	timestamp = DateTime.UtcNow
}));

app.MapControllers();
app.MapHub<RealtimeHub>("/hubs/realtime");
app.MapHub<CallHub>("/hubs/calls");

app.Run();
