using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

const string ExtensionName = "com.example/report-export";

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseTestServer();
builder.Services.AddSingleton<CapabilityBarrier>();
builder.Services
    .AddMcpServer()
    .WithHttpTransport(options => options.Stateless = true)
    .WithTools<CapabilityTools>();

await using var app = builder.Build();
app.MapMcp("/mcp");
await app.StartAsync();

using var client = app.GetTestClient();
var enabledRequest = SendAsync(client, "enabled-request", includeExtension: true, includeCapabilities: true);
var disabledRequest = SendAsync(client, "disabled-request", includeExtension: false, includeCapabilities: true);
var responses = await Task.WhenAll(enabledRequest, disabledRequest);

var enabled = ReadToolText(responses.Single(response => response.Name == "enabled-request"));
var disabled = ReadToolText(responses.Single(response => response.Name == "disabled-request"));

Check(enabled == "enabled-request:request=enabled,server=null", "enabled request kept its capability");
Check(disabled == "disabled-request:request=disabled,server=null", "disabled request did not inherit another request's capability");

var rejected = await SendAsync(client, "missing-capabilities", includeExtension: false, includeCapabilities: false);
using var rejectedJson = JsonDocument.Parse(ReadJsonBody(rejected.Body));
var errorCode = rejectedJson.RootElement.GetProperty("error").GetProperty("code").GetInt32();
Check(rejected.StatusCode == HttpStatusCode.BadRequest, "missing capabilities returned HTTP 400");
Check(errorCode == -32602, "missing capabilities returned MCP -32602");

Console.WriteLine("Verifier passed 6/6.");

static async Task<Response> SendAsync(
    HttpClient client,
    string name,
    bool includeExtension,
    bool includeCapabilities)
{
    var metadata = new JsonObject
    {
        ["io.modelcontextprotocol/protocolVersion"] = "2026-07-28",
        ["io.modelcontextprotocol/clientInfo"] = new JsonObject
        {
            ["name"] = "offline-verifier",
            ["version"] = "1.0.0",
        },
    };

    if (includeCapabilities)
    {
        var extensions = new JsonObject();
        if (includeExtension)
        {
            extensions[ExtensionName] = new JsonObject();
        }

        metadata["io.modelcontextprotocol/clientCapabilities"] = new JsonObject
        {
            ["extensions"] = extensions,
        };
    }

    var payload = new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = name,
        ["method"] = "tools/call",
        ["params"] = new JsonObject
        {
            ["name"] = "inspect_client_capability",
            ["arguments"] = new JsonObject { ["requestName"] = name },
            ["_meta"] = metadata,
        },
    }.ToJsonString();

    using var request = new HttpRequestMessage(HttpMethod.Post, "/mcp")
    {
        Content = new StringContent(payload, Encoding.UTF8, "application/json"),
    };
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("text/event-stream"));
    request.Headers.Add("MCP-Protocol-Version", "2026-07-28");
    request.Headers.Add("Mcp-Method", "tools/call");
    request.Headers.Add("Mcp-Name", "inspect_client_capability");

    using var response = await client.SendAsync(request);
    return new Response(name, response.StatusCode, await response.Content.ReadAsStringAsync());
}

static string ReadToolText(Response response)
{
    Check(response.StatusCode == HttpStatusCode.OK, $"{response.Name} returned HTTP 200");
    using var document = JsonDocument.Parse(ReadJsonBody(response.Body));
    return document.RootElement
        .GetProperty("result")
        .GetProperty("content")[0]
        .GetProperty("text")
        .GetString()!;
}

static string ReadJsonBody(string body)
{
    var data = Regex.Match(body, "(?m)^data:\\s*(.+)$");
    return data.Success ? data.Groups[1].Value : body;
}

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {message}");
    }

    Console.WriteLine($"PASS: {message}");
}

internal sealed record Response(string Name, HttpStatusCode StatusCode, string Body);

internal sealed class CapabilityBarrier
{
    private readonly TaskCompletionSource _bothArrived =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
    private int _arrivalCount;

    public async Task WaitForBothAsync(CancellationToken cancellationToken)
    {
        if (Interlocked.Increment(ref _arrivalCount) == 2)
        {
            _bothArrived.TrySetResult();
        }

        await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(5), cancellationToken);
    }
}

internal sealed class CapabilityTools
{
    private const string RequiredExtension = "com.example/report-export";

    [McpServerTool(Name = "inspect_client_capability", ReadOnly = true, OpenWorld = false)]
    [Description("Reports whether this request declared the report-export extension.")]
    public async Task<string> InspectClientCapabilityAsync(
        [Description("A stable identifier used by the verifier.")] string requestName,
        RequestContext<CallToolRequestParams> request,
        CapabilityBarrier barrier,
        CancellationToken cancellationToken)
    {
        await barrier.WaitForBothAsync(cancellationToken);

        var requestCapabilities = request.JsonRpcRequest.Context?.ClientCapabilities;
        var enabled = requestCapabilities?.Extensions?.ContainsKey(RequiredExtension) is true;
        var serverValue = request.Server.ClientCapabilities is null ? "null" : "set";

        return $"{requestName}:request={(enabled ? "enabled" : "disabled")},server={serverValue}";
    }
}
