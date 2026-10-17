using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

// Independent fixture: never references application configuration, services or databases.
if (args.Length != 1) throw new ArgumentException("Supply the absolute miniapp-sdk directory.");
var root = Path.GetFullPath(args[0]);
if (!File.Exists(Path.Combine(root, "package.json")) || !File.Exists(Path.Combine(root, "dist", "bridge.js")))
    throw new ArgumentException("Build miniapp-sdk before starting the harness.");
using var key = RSA.Create(2048);
var request = new CertificateRequest("CN=localhost", key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
var names = new SubjectAlternativeNameBuilder(); names.AddDnsName("localhost"); names.AddIpAddress(IPAddress.Loopback);
request.CertificateExtensions.Add(names.Build());
request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment, true));
request.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension(new OidCollection {new("1.3.6.1.5.5.7.3.1")}, true));
using var generated = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddMinutes(-5), DateTimeOffset.UtcNow.AddHours(2));
// Match the existing Windows HTTPS fixture's Schannel-compatible key import.
using var certificate = new X509Certificate2(generated.Export(X509ContentType.Pfx), (string?)null, X509KeyStorageFlags.Exportable);
var builder = WebApplication.CreateBuilder(new WebApplicationOptions { Args = [], EnvironmentName = "Development" });
builder.Configuration.Sources.Clear(); builder.Logging.ClearProviders();
builder.WebHost.ConfigureKestrel(options =>
{
    options.Listen(IPAddress.Loopback, 5461, listen => listen.UseHttps(certificate));
    options.Listen(IPAddress.Loopback, 5462, listen => listen.UseHttps(certificate));
});
await using var app = builder.Build();
app.MapGet("/{**path}", (string? path, HttpContext context) =>
{
    context.Response.Headers.CacheControl = "no-store";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers.ContentSecurityPolicy = "default-src 'none'; script-src 'self'; style-src 'self'; img-src data:; frame-src https://localhost:5462; frame-ancestors https://localhost:5461; base-uri 'none'; form-action 'none'";
    var allowed = new HashSet<string> { "host.html", "child.html", "harness-host.js", "harness-child.js", "harness.css", "dist/bridge.js", "dist/config.js" };
    if (path == null || !allowed.Contains(path)) return Results.NotFound();
    var file = Path.Combine(root, path.StartsWith("dist/") ? path : "examples/bridge/" + path);
    var type = path.EndsWith(".html") ? "text/html; charset=utf-8" : path.EndsWith(".css") ? "text/css" : "text/javascript";
    return Results.File(file, type);
});
Console.WriteLine("Controlled HTTPS bridge harness: https://localhost:5461/host.html (ephemeral certificate; loopback only)");
await app.RunAsync();
