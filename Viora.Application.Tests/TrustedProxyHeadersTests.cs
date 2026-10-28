using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Collections.Concurrent;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using viora_BE.Hosting;
using Xunit;

public sealed class TrustedProxyHeadersTests
{
    [Theory]
    [InlineData(null, false)]
    [InlineData("https", false)]
    [InlineData("http", true)]
    public async Task HttpBridgeReportsPeerEvenWhenProtoIsMissingOrConsumed(string? proto, bool trusted)
    {
        var capture = new ProxyLogCapture();
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders(); builder.Logging.AddProvider(capture);
        if (trusted) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { ["ReverseProxy:KnownProxies:0"] = "10.20.30.40" });
        builder.Services.AddTrustedProxyHeaders(builder.Configuration);
        await using var app = builder.Build();
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse("10.20.30.40"); return next(context); });
        app.UseTrustedProxyHeaders();
        app.MapGet("/api/miniapp/auth/exchange", (HttpContext context) => context.Request.Scheme);
        await app.StartAsync();
        using var client = app.GetTestClient(); client.BaseAddress = new Uri("http://issuer.example");
        if (proto is not null) client.DefaultRequestHeaders.Add("X-Forwarded-Proto", proto);
        client.DefaultRequestHeaders.Add("Cookie", "fixture=private-cookie-fixture");
        for (var request = 0; request < 2; request++)
            Assert.Equal("http", await client.GetStringAsync("/api/miniapp/auth/exchange?token=private-query-fixture"));
        var message = Assert.Single(capture.Messages, value => value.Contains("Immediate peer:"));
        Assert.Contains("10.20.30.40", message);
        Assert.Contains("Forwarded proto present: " + (proto is not null), message);
        Assert.Contains(capture.Messages, value => value.Contains("Trusted proxy HTTPS forwarding is active"));
        Assert.All(capture.Messages, value =>
        {
            Assert.DoesNotContain("private-query-fixture", value);
            Assert.DoesNotContain("private-cookie-fixture", value);
        });
    }

    private sealed class ProxyLogCapture : ILoggerProvider, ILogger
    {
        public ConcurrentQueue<string> Messages { get; } = new();
        public ILogger CreateLogger(string categoryName) => categoryName == "ANKT.ReverseProxy" ? this : Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) => Messages.Enqueue(formatter(state, exception));
        public void Dispose() { }
    }

    [Theory]
    [InlineData("10.20.30.40", "https", "https", "10.20.30.40")]
    [InlineData("203.0.113.9", "https", "http", "10.20.30.40")]
    [InlineData("10.20.30.40", "https, http", "http", "10.20.30.40")]
    [InlineData("10.20.30.40", "https", "http", "")]
    [InlineData("10.20.30.40", "https", "https", "10.20.30.0/24")]
    [InlineData("10.20.31.40", "https", "http", "10.20.30.0/24")]
    [InlineData("::ffff:10.20.30.40", "https", "https", "10.20.30.40")]
    public async Task OnlyConfiguredImmediateProxyCanSupplyTheOriginalScheme(string peer, string header, string expected, string trust)
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer(); builder.Logging.ClearProviders();
        if (trust.Length > 0) builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
            { [trust.Contains('/') ? "ReverseProxy:KnownNetworks:0" : "ReverseProxy:KnownProxies:0"] = trust });
        builder.Services.AddTrustedProxyHeaders(builder.Configuration);
        await using var app = builder.Build();
        app.Use((context, next) => { context.Connection.RemoteIpAddress = IPAddress.Parse(peer); return next(context); });
        app.UseTrustedProxyHeaders();
        app.MapGet("/", (HttpContext context) => context.Request.Scheme + "|" + context.Request.Host.Value + "|" + context.Connection.RemoteIpAddress);
        await app.StartAsync();
        using var client = app.GetTestClient(); client.BaseAddress = new Uri("http://issuer.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-Proto", header);
        client.DefaultRequestHeaders.Add("X-Forwarded-Host", "attacker.example");
        client.DefaultRequestHeaders.Add("X-Forwarded-For", "198.51.100.99");
        Assert.Equal(expected + "|issuer.example|" + peer, await client.GetStringAsync("/"));
    }

    [Theory]
    [InlineData("ReverseProxy:KnownProxies:0", "not-an-ip")]
    [InlineData("ReverseProxy:KnownProxies:0", "0.0.0.0")]
    [InlineData("ReverseProxy:KnownNetworks:0", "0.0.0.0/0")]
    [InlineData("ReverseProxy:KnownNetworks:0", "::/0")]
    public void InvalidOrUnboundedTrustConfigurationFailsAtStartup(string key, string value)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { [key] = value }).Build();
        Assert.Throws<InvalidOperationException>(() => new ServiceCollection().AddTrustedProxyHeaders(configuration));
    }

}
