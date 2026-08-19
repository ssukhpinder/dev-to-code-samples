using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Microsoft.AspNetCore.TestHost;
using ModelContextProtocol.AspNetCore;
using ModelContextProtocol.Server;

WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    EnvironmentName = "Testing"
});

builder.Logging.ClearProviders();
builder.WebHost.UseTestServer();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options =>
    {
        options.SessionMode = HttpServerSessionMode.StatefulForInitializeClients;
    })
    .WithTools<DemoTools>();

WebApplication app = builder.Build();
app.MapMcp("/mcp");
await app.StartAsync();

try
{
    using HttpClient client = app.GetTestClient();

    using HttpResponseMessage modernDiscovery = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "id": 1,
          "method": "server/discover",
          "params": {
            "_meta": {
              "io.modelcontextprotocol/protocolVersion": "2026-07-28",
              "io.modelcontextprotocol/clientCapabilities": {},
              "io.modelcontextprotocol/clientInfo": {
                "name": "hybrid-probe",
                "version": "1.0.0"
              }
            }
          }
        }
        """,
        protocolVersion: "2026-07-28",
        method: "server/discover");

    await AssertSuccessAsync(modernDiscovery, "modern discovery");
    AssertNoSession(modernDiscovery, "modern discovery");
    Pass("modern 2026-07-28 discovery returned no session");

    using HttpResponseMessage modernToolCall = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "id": 2,
          "method": "tools/call",
          "params": {
            "name": "echo",
            "arguments": { "message": "modern" },
            "_meta": {
              "io.modelcontextprotocol/protocolVersion": "2026-07-28",
              "io.modelcontextprotocol/clientCapabilities": {},
              "io.modelcontextprotocol/clientInfo": {
                "name": "hybrid-probe",
                "version": "1.0.0"
              }
            }
          }
        }
        """,
        protocolVersion: "2026-07-28",
        method: "tools/call",
        name: "echo");

    await AssertBodyContainsAsync(modernToolCall, "modern", "modern tool call");
    AssertNoSession(modernToolCall, "modern tool call");
    Pass("modern tool call stayed stateless");

    using HttpResponseMessage legacyInitialize = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "id": 3,
          "method": "initialize",
          "params": {
            "protocolVersion": "2025-11-25",
            "capabilities": {},
            "clientInfo": { "name": "hybrid-probe", "version": "1.0.0" }
          }
        }
        """);

    await AssertSuccessAsync(legacyInitialize, "legacy initialize");
    string sessionId = GetRequiredSessionId(legacyInitialize, "legacy initialize");
    Pass("legacy initialize minted a session");

    using HttpResponseMessage initializedNotification = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "method": "notifications/initialized",
          "params": {}
        }
        """,
        protocolVersion: "2025-11-25",
        sessionId: sessionId);

    if (initializedNotification.StatusCode is not HttpStatusCode.Accepted and not HttpStatusCode.NoContent)
    {
        await ThrowUnexpectedAsync(initializedNotification, "legacy initialized notification");
    }

    using HttpResponseMessage legacyToolCall = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "id": 4,
          "method": "tools/call",
          "params": {
            "name": "echo",
            "arguments": { "message": "legacy" }
          }
        }
        """,
        protocolVersion: "2025-11-25",
        sessionId: sessionId);

    await AssertBodyContainsAsync(legacyToolCall, "legacy", "legacy tool call");
    Equal(sessionId, GetRequiredSessionId(legacyToolCall, "legacy tool call"));
    Pass("legacy tool call reused its session");

    using HttpRequestMessage modernDeleteRequest = new(HttpMethod.Delete, "/mcp");
    modernDeleteRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2026-07-28");
    using HttpResponseMessage modernDelete = await client.SendAsync(modernDeleteRequest);
    Equal(HttpStatusCode.MethodNotAllowed, modernDelete.StatusCode);
    AssertNoSession(modernDelete, "modern DELETE");
    Pass("modern DELETE was rejected without session state");

    using HttpRequestMessage legacyDeleteRequest = new(HttpMethod.Delete, "/mcp");
    legacyDeleteRequest.Headers.TryAddWithoutValidation("MCP-Protocol-Version", "2025-11-25");
    legacyDeleteRequest.Headers.TryAddWithoutValidation("Mcp-Session-Id", sessionId);
    using HttpResponseMessage legacyDelete = await client.SendAsync(legacyDeleteRequest);

    if (!legacyDelete.IsSuccessStatusCode)
    {
        await ThrowUnexpectedAsync(legacyDelete, "legacy DELETE");
    }

    using HttpResponseMessage deletedSessionPing = await SendRpcAsync(
        client,
        """
        {
          "jsonrpc": "2.0",
          "id": 5,
          "method": "ping"
        }
        """,
        protocolVersion: "2025-11-25",
        sessionId: sessionId);

    Equal(HttpStatusCode.NotFound, deletedSessionPing.StatusCode);
    Pass("legacy DELETE closed the session");
    Console.WriteLine("6/6 checks passed");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static async Task<HttpResponseMessage> SendRpcAsync(
    HttpClient client,
    string json,
    string? protocolVersion = null,
    string? method = null,
    string? name = null,
    string? sessionId = null)
{
    using HttpRequestMessage request = new(HttpMethod.Post, "/mcp")
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json")
    };

    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));

    AddHeader(request, "MCP-Protocol-Version", protocolVersion);
    AddHeader(request, "Mcp-Method", method);
    AddHeader(request, "Mcp-Name", name);
    AddHeader(request, "Mcp-Session-Id", sessionId);

    return await client.SendAsync(request);
}

static void AddHeader(HttpRequestMessage request, string name, string? value)
{
    if (value is not null)
    {
        request.Headers.TryAddWithoutValidation(name, value);
    }
}

static async Task AssertSuccessAsync(HttpResponseMessage response, string operation)
{
    if (!response.IsSuccessStatusCode)
    {
        await ThrowUnexpectedAsync(response, operation);
    }
}

static async Task AssertBodyContainsAsync(
    HttpResponseMessage response,
    string expected,
    string operation)
{
    await AssertSuccessAsync(response, operation);
    string body = await response.Content.ReadAsStringAsync();

    if (!body.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            $"{operation} response did not contain '{expected}'. Body: {body}");
    }
}

static async Task ThrowUnexpectedAsync(HttpResponseMessage response, string operation)
{
    string body = await response.Content.ReadAsStringAsync();
    throw new InvalidOperationException(
        $"{operation} returned {(int)response.StatusCode} {response.StatusCode}. Body: {body}");
}

static string GetRequiredSessionId(HttpResponseMessage response, string operation)
{
    if (!response.Headers.TryGetValues("Mcp-Session-Id", out IEnumerable<string>? values))
    {
        throw new InvalidOperationException($"{operation} did not return Mcp-Session-Id.");
    }

    string? sessionId = values.SingleOrDefault();
    return string.IsNullOrWhiteSpace(sessionId)
        ? throw new InvalidOperationException($"{operation} returned an empty Mcp-Session-Id.")
        : sessionId;
}

static void AssertNoSession(HttpResponseMessage response, string operation)
{
    if (response.Headers.Contains("Mcp-Session-Id"))
    {
        throw new InvalidOperationException($"{operation} unexpectedly returned Mcp-Session-Id.");
    }
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Pass(string message) => Console.WriteLine($"PASS: {message}");

[McpServerToolType]
public sealed class DemoTools
{
    [McpServerTool(Name = "echo"), Description("Echoes a message for transport verification.")]
    public static string Echo([Description("The message to return.")] string message) => message;
}
