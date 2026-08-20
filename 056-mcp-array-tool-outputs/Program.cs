using System.IO.Pipelines;
using System.Text.Json;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

const string ModernProtocol = "2026-07-28";
const string LegacyProtocol = "2025-11-25";

var checks = new List<string>();

await VerifyWireShapeAsync(ModernProtocol, expectNaturalOutput: true, checks);
await VerifyWireShapeAsync(LegacyProtocol, expectNaturalOutput: false, checks);

Console.WriteLine($"PASS {checks.Count}/{checks.Count}");
foreach (string check in checks)
{
    Console.WriteLine($"  {check}");
}

static async Task VerifyWireShapeAsync(
    string protocolVersion,
    bool expectNaturalOutput,
    List<string> checks)
{
    var clientToServer = new Pipe();
    var serverToClient = new Pipe();

    await using Stream serverInput = clientToServer.Reader.AsStream();
    await using Stream serverOutput = serverToClient.Writer.AsStream();
    await using Stream clientInput = serverToClient.Reader.AsStream();
    await using Stream clientOutput = clientToServer.Writer.AsStream();

    await using var serverTransport = new StreamServerTransport(
        serverInput,
        serverOutput,
        $"server-{protocolVersion}");

    var toolCollection = new McpServerPrimitiveCollection<McpServerTool>(StringComparer.Ordinal);
    toolCollection.Add(McpServerTool.Create(
        (Func<string[]>)ListTiers,
        new McpServerToolCreateOptions
        {
            Name = "list_tiers",
            Description = "Returns the supported service tiers.",
            UseStructuredContent = true,
            ReadOnly = true,
        }));
    toolCollection.Add(McpServerTool.Create(
        (Func<int>)CountTiers,
        new McpServerToolCreateOptions
        {
            Name = "count_tiers",
            Description = "Returns the number of supported service tiers.",
            UseStructuredContent = true,
            ReadOnly = true,
        }));

    await using McpServer server = McpServer.Create(
        serverTransport,
        new McpServerOptions
        {
            ServerInfo = new Implementation { Name = "natural-output-verifier", Version = "1.0.0" },
            Capabilities = new ServerCapabilities { Tools = new ToolsCapability() },
            ToolCollection = toolCollection,
        });

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    Task serverTask = server.RunAsync(timeout.Token);

    var clientTransport = new StreamClientTransport(clientOutput, clientInput);
    await using (McpClient client = await McpClient.CreateAsync(
        clientTransport,
        new McpClientOptions
        {
            ClientInfo = new Implementation { Name = "wire-contract-test", Version = "1.0.0" },
            ProtocolVersion = protocolVersion,
        },
        cancellationToken: timeout.Token))
    {
        Check(
            client.NegotiatedProtocolVersion == protocolVersion,
            $"{protocolVersion}: negotiated requested protocol",
            checks);

        IList<McpClientTool> tools = await client.ListToolsAsync(cancellationToken: timeout.Token);
        McpClientTool arrayTool = tools.Single(tool => tool.Name == "list_tiers");
        McpClientTool scalarTool = tools.Single(tool => tool.Name == "count_tiers");

        JsonElement arraySchema = arrayTool.ProtocolTool.OutputSchema!.Value;
        JsonElement scalarSchema = scalarTool.ProtocolTool.OutputSchema!.Value;
        CallToolResult arrayResult = await arrayTool.CallAsync(cancellationToken: timeout.Token);
        CallToolResult scalarResult = await scalarTool.CallAsync(cancellationToken: timeout.Token);

        if (expectNaturalOutput)
        {
            Check(arraySchema.GetProperty("type").GetString() == "array", "modern: array schema has an array root", checks);
            Check(scalarSchema.GetProperty("type").GetString() == "integer", "modern: scalar schema has an integer root", checks);
            Check(arrayResult.StructuredContent?.ValueKind == JsonValueKind.Array, "modern: array result has no result wrapper", checks);
            Check(scalarResult.StructuredContent?.GetInt32() == 3, "modern: scalar result has no result wrapper", checks);
            Check(
                !TryReadLegacyResult(arrayResult.StructuredContent!.Value, out _),
                "modern: legacy result-wrapper parser is rejected",
                checks);
        }
        else
        {
            Check(IsLegacyEnvelope(arraySchema, "array"), "legacy: array schema advertises a result envelope", checks);
            Check(IsLegacyEnvelope(scalarSchema, "integer"), "legacy: scalar schema advertises a result envelope", checks);
            Check(arrayResult.StructuredContent?.GetProperty("result").ValueKind == JsonValueKind.Array, "legacy: array result keeps the result envelope", checks);
            Check(scalarResult.StructuredContent?.GetProperty("result").GetInt32() == 3, "legacy: scalar result keeps the result envelope", checks);
        }

        Check(
            ReadArray(expectNaturalOutput ? arrayResult.StructuredContent!.Value : arrayResult.StructuredContent!.Value.GetProperty("result"))
                .SequenceEqual(ListTiers(), StringComparer.Ordinal),
            $"{protocolVersion}: array values survive serialization",
            checks);

        Check(
            arrayResult.Content.OfType<TextContentBlock>().Any(block => block.Text.Contains("starter", StringComparison.Ordinal)),
            $"{protocolVersion}: text fallback remains available",
            checks);
    }

    timeout.Cancel();

    try
    {
        await serverTask;
    }
    catch (OperationCanceledException)
    {
    }
}

static bool IsLegacyEnvelope(JsonElement schema, string innerType) =>
    schema.GetProperty("type").GetString() == "object" &&
    schema.GetProperty("properties").GetProperty("result").GetProperty("type").GetString() == innerType &&
    schema.GetProperty("required").EnumerateArray().Any(value => value.GetString() == "result");

static bool TryReadLegacyResult(JsonElement value, out JsonElement result)
{
    if (value.ValueKind == JsonValueKind.Object && value.TryGetProperty("result", out result))
    {
        return true;
    }

    result = default;
    return false;
}

static string[] ReadArray(JsonElement value) =>
    value.EnumerateArray().Select(item => item.GetString()!).ToArray();

static string[] ListTiers() => ["starter", "growth", "enterprise"];

static int CountTiers() => ListTiers().Length;

static void Check(bool condition, string message, List<string> checks)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {message}");
    }

    checks.Add(message);
}
