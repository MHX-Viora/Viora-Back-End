using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Npgsql;

// Starts its own cluster; never accepts a connection string or contacts an installed service.
internal sealed class IsolatedPostgres : IAsyncDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "ankt-oidc-pg-" + Guid.NewGuid().ToString("N"));
    private readonly string binaries = Environment.GetEnvironmentVariable("ANKT_TEST_PG_BIN") ?? "C:/Program Files/PostgreSQL/18/bin";
    private bool started;
    public string ConnectionString { get; private set; } = "";

    public static async Task<IsolatedPostgres> Start()
    {
        var cluster = new IsolatedPostgres();
        try
        {
            Directory.CreateDirectory(cluster.root);
            await cluster.Run("initdb", "-D", Path.Combine(cluster.root, "data"), "-U", "ankt_test", "-A", "trust", "--no-locale", "--encoding=UTF8");
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start(); var port = ((IPEndPoint)listener.LocalEndpoint).Port; listener.Stop();
            cluster.started = true;
            await cluster.Run("pg_ctl", "-D", Path.Combine(cluster.root, "data"), "-l", Path.Combine(cluster.root, "server.log"),
                "-o", $"-h 127.0.0.1 -p {port}", "-w", "start");
            cluster.ConnectionString = new NpgsqlConnectionStringBuilder
            { Host = "127.0.0.1", Port = port, Database = "postgres", Username = "ankt_test", Pooling = false }.ConnectionString;
            return cluster;
        }
        catch { await cluster.DisposeAsync(); throw; }
    }

    private async Task Run(string tool, params string[] arguments)
    {
        var start = new ProcessStartInfo(Path.Combine(binaries, tool + (OperatingSystem.IsWindows() ? ".exe" : "")))
        { UseShellExecute = false, CreateNoWindow = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Cannot start isolated PostgreSQL.");
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch { process.Kill(true); throw; }
        if (process.ExitCode != 0) throw new InvalidOperationException($"Isolated PostgreSQL {tool} failed with exit code {process.ExitCode}.");
    }

    public async ValueTask DisposeAsync()
    {
        if (started) await Run("pg_ctl", "-D", Path.Combine(root, "data"), "-m", "immediate", "-w", "stop");
        // root is an internally generated GUID directory, never a supplied path.
        if (Directory.Exists(root)) Directory.Delete(root, true);
    }
}
