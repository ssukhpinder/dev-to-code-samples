using System.IO.Pipelines;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

const string ModernProtocol = "2026-07-28";
const string LegacyProtocol = "2025-11-25";

var checks = new List<string>();

await VerifyResourceContractAsync(
    ModernProtocol,
    McpErrorCode.InvalidParams,
    "modern",
    checks);

await VerifyResourceContractAsync(
    LegacyProtocol,
    McpErrorCode.ResourceNotFound,
    "legacy",
    checks);

Check(
    !IsMissingResource(new McpProtocolException("Internal failure", McpErrorCode.InternalError)),
    "classifier rejects unrelated protocol errors",
    checks);

Console.WriteLine($"PASS {checks.Count}/{checks.Count}");
foreach (string check in checks)
{
    Console.WriteLine($"  {check}");
}

static async Task VerifyResourceContractAsync(
    string protocolVersion,
    McpErrorCode expectedMissingCode,
    string label,
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
        $"resource-server-{protocolVersion}");

    var resources = new McpServerResourceCollection
    {
        McpServerResource.Create(
            (Func<string>)(() => "starter|growth|enterprise"),
            new McpServerResourceCreateOptions
            {
                UriTemplate = "catalog://tiers",
                Name = "service-tiers",
                Description = "The supported service tiers.",
                MimeType = "text/plain",
            }),
        McpServerResource.Create(
            (Func<IEnumerable<string>>)(() => Array.Empty<string>()),
            new McpServerResourceCreateOptions
            {
                UriTemplate = "catalog://announcements",
                Name = "announcements",
                Description = "Current announcements; an empty result is valid.",
                MimeType = "text/plain",
            }),
    };

    await using McpServer server = McpServer.Create(
        serverTransport,
        new McpServerOptions
        {
            ServerInfo = new Implementation
            {
                Name = "resource-contract-verifier",
                Version = "1.0.0",
            },
            Capabilities = new ServerCapabilities
            {
                Resources = new ResourcesCapability(),
            },
            ResourceCollection = resources,
        });

    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
    Task serverTask = server.RunAsync(timeout.Token);

    var clientTransport = new StreamClientTransport(clientOutput, clientInput);
    await using (McpClient client = await McpClient.CreateAsync(
        clientTransport,
        new McpClientOptions
        {
            ClientInfo = new Implementation
            {
                Name = "resource-contract-test",
                Version = "1.0.0",
            },
            ProtocolVersion = protocolVersion,
        },
        cancellationToken: timeout.Token))
    {
        Check(
            client.NegotiatedProtocolVersion == protocolVersion,
            $"{label}: negotiated {protocolVersion}",
            checks);

        IList<McpClientResource> listed = await client.ListResourcesAsync(
            cancellationToken: timeout.Token);

        Check(
            listed.Select(resource => resource.Uri).Order(StringComparer.Ordinal).SequenceEqual(
                ["catalog://announcements", "catalog://tiers"],
                StringComparer.Ordinal),
            $"{label}: lists both real resources",
            checks);

        ReadResourceResult tiers = await client.ReadResourceAsync(
            "catalog://tiers",
            cancellationToken: timeout.Token);

        Check(
            tiers.Contents is [TextResourceContents { Text: "starter|growth|enterprise" }],
            $"{label}: reads known resource content",
            checks);

        ReadResourceResult empty = await client.ReadResourceAsync(
            "catalog://announcements",
            cancellationToken: timeout.Token);

        Check(
            empty.Contents.Count == 0,
            $"{label}: preserves valid empty contents",
            checks);

        McpProtocolException? missing = null;
        try
        {
            _ = await client.ReadResourceAsync(
                "catalog://missing",
                cancellationToken: timeout.Token);
        }
        catch (McpProtocolException exception)
        {
            missing = exception;
        }

        Check(missing is not null, $"{label}: missing resource throws", checks);
        Check(
            missing!.ErrorCode == expectedMissingCode,
            $"{label}: missing resource uses {(int)expectedMissingCode}",
            checks);
        Check(
            IsMissingResource(missing),
            $"{label}: client classifier accepts missing-resource code",
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

static bool IsMissingResource(McpProtocolException exception) =>
    exception.ErrorCode is McpErrorCode.InvalidParams or McpErrorCode.ResourceNotFound;

static void Check(bool condition, string message, List<string> checks)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {message}");
    }

    checks.Add(message);
}
