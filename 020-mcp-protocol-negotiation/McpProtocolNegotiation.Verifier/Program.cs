using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Client;

const string ModernProtocol = "2026-07-28";

var checks = new List<(string Name, Func<Task> Run)>
{
    ("default client uses the modern protocol with a stateless server", DefaultClientUsesModernProtocolAsync),
    ("default client falls back when the server requires a session", DefaultClientFallsBackAsync),
    ("pinned modern client connects to a stateless server", PinnedModernClientConnectsAsync),
    ("pinned modern client rejects a stateful server", PinnedModernClientRejectsFallbackAsync),
};

var failures = new List<string>();

foreach (var check in checks)
{
    try
    {
        await check.Run();
        Console.WriteLine($"PASS: {check.Name}");
    }
    catch (Exception exception)
    {
        failures.Add($"FAIL: {check.Name}{Environment.NewLine}{exception}");
    }
}

if (failures.Count > 0)
{
    Console.Error.WriteLine(string.Join(Environment.NewLine, failures));
    return 1;
}

Console.WriteLine($"Verified {checks.Count}/{checks.Count} protocol-negotiation scenarios.");
return 0;

static async Task DefaultClientUsesModernProtocolAsync()
{
    await using var server = await LocalMcpServer.StartAsync(stateless: true);
    await using var client = await ConnectAsync(server.Endpoint);

    Expect(client.NegotiatedProtocolVersion == ModernProtocol,
        $"Expected {ModernProtocol}, got {client.NegotiatedProtocolVersion ?? "<null>"}.");
    Expect(client.SessionId is null, "A modern stateless connection must not mint an MCP session ID.");
}

static async Task DefaultClientFallsBackAsync()
{
    await using var server = await LocalMcpServer.StartAsync(stateless: false);
    await using var client = await ConnectAsync(server.Endpoint);

    Expect(client.NegotiatedProtocolVersion != ModernProtocol,
        "A stateful HTTP server must not negotiate the stateless 2026-07-28 protocol.");
    Expect(!string.IsNullOrWhiteSpace(client.SessionId),
        "The legacy fallback should establish an MCP session.");
}

static async Task PinnedModernClientConnectsAsync()
{
    await using var server = await LocalMcpServer.StartAsync(stateless: true);
    await using var client = await ConnectAsync(server.Endpoint, ModernProtocol);

    Expect(client.NegotiatedProtocolVersion == ModernProtocol,
        $"Expected pinned protocol {ModernProtocol}, got {client.NegotiatedProtocolVersion ?? "<null>"}.");
    Expect(client.SessionId is null, "A pinned modern connection must remain sessionless.");
}

static async Task PinnedModernClientRejectsFallbackAsync()
{
    await using var server = await LocalMcpServer.StartAsync(stateless: false);

    try
    {
        await using var unexpectedClient = await ConnectAsync(server.Endpoint, ModernProtocol);
        throw new InvalidOperationException(
            $"The client unexpectedly downgraded to {unexpectedClient.NegotiatedProtocolVersion ?? "<null>"}.");
    }
    catch (McpException)
    {
    }
}

static async Task<McpClient> ConnectAsync(Uri endpoint, string? pinnedProtocol = null)
{
    var transport = new HttpClientTransport(new HttpClientTransportOptions
    {
        Endpoint = endpoint,
        TransportMode = HttpTransportMode.StreamableHttp,
    });

    var options = pinnedProtocol is null
        ? null
        : new McpClientOptions { ProtocolVersion = pinnedProtocol };

    return await McpClient.CreateAsync(transport, options);
}

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

file sealed class LocalMcpServer(WebApplication app, Uri endpoint) : IAsyncDisposable
{
    public Uri Endpoint { get; } = endpoint;

    public static async Task<LocalMcpServer> StartAsync(bool stateless)
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Logging.ClearProviders();
        builder.Services
            .AddMcpServer()
            .WithHttpTransport(options => options.Stateless = stateless);

        var app = builder.Build();
        app.MapMcp("/mcp");
        await app.StartAsync();

        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses;

        var address = addresses?.SingleOrDefault()
            ?? throw new InvalidOperationException("Kestrel did not report its loopback address.");
        var endpoint = new Uri(new Uri(address), "mcp");

        return new LocalMcpServer(app, endpoint);
    }

    public async ValueTask DisposeAsync()
    {
        await app.StopAsync();
        await app.DisposeAsync();
    }
}
