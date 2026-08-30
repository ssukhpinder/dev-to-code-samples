using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using ModelContextProtocol;
using ModelContextProtocol.Client;
using ModelContextProtocol.Extensions.Tasks;
using ModelContextProtocol.Protocol;

const int StuckPollLimit = 3;

int elicitationCalls = 0;
StuckTaskTransport server = new();
InMemoryClientTransport transport = new(server);

McpClientOptions options = new()
{
    ProtocolVersion = "2026-07-28",
    ClientInfo = new Implementation
    {
        Name = "stuck-task-verifier",
        Version = "1.0.0",
    },
    Handlers = new McpClientHandlers
    {
        ElicitationHandler = (request, cancellationToken) =>
        {
            elicitationCalls++;
            return ValueTask.FromResult(new ElicitResult { Action = "decline" });
        },
    },
};

await using McpClient client = await McpClient.CreateAsync(transport, options);

bool guardThrew = false;
try
{
    await client.CallToolWithPollingAsync(
        new CallToolRequestParams { Name = "stuck-tool" },
        maxConsecutiveStuckPolls: StuckPollLimit,
        cancellationToken: CancellationToken.None);
}
catch (McpException)
{
    guardThrew = true;
}

Verify(guardThrew, "stuck poll guard raised McpException");
Verify(elicitationCalls == 1, "repeated input key was presented once");
Verify(
    server.GetCalls == StuckPollLimit + 1,
    $"threshold {StuckPollLimit} stopped polling after {server.GetCalls} tasks/get calls");
Verify(
    server.UpdateCalls == 1 && server.UpdateEnvelopeValid,
    "declined approval response targeted task-1 once");
Verify(
    server.CancelCalls == 1 && server.CancelEnvelopeValid,
    "best-effort tasks/cancel targeted task-1 once");

static void Verify(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {message}");
    }

    Console.WriteLine($"PASS: {message}");
}

sealed class InMemoryClientTransport(StuckTaskTransport transport) : IClientTransport
{
    public string Name => "in-memory-stuck-task";

    public Task<ITransport> ConnectAsync(CancellationToken cancellationToken) =>
        Task.FromResult<ITransport>(transport);
}

sealed class StuckTaskTransport : ITransport
{
    private static readonly DateTimeOffset FixedTime =
        DateTimeOffset.Parse("2026-08-30T00:00:00Z");

    private readonly Channel<JsonRpcMessage> _messages =
        Channel.CreateUnbounded<JsonRpcMessage>();

    public ChannelReader<JsonRpcMessage> MessageReader => _messages.Reader;
    public string? SessionId => null;
    public int GetCalls { get; private set; }
    public int UpdateCalls { get; private set; }
    public int CancelCalls { get; private set; }
    public bool UpdateEnvelopeValid { get; private set; }
    public bool CancelEnvelopeValid { get; private set; }

    public Task SendMessageAsync(
        JsonRpcMessage message,
        CancellationToken cancellationToken)
    {
        if (message is not JsonRpcRequest request)
        {
            return Task.CompletedTask;
        }

        JsonNode result = request.Method switch
        {
            "server/discover" => Discover(),
            "tools/call" => CreateTask(),
            "tasks/get" => GetTask(),
            "tasks/update" => UpdateTask(request),
            "tasks/cancel" => CancelTask(request),
            _ => throw new InvalidOperationException(
                $"Unexpected method: {request.Method}"),
        };

        _messages.Writer.TryWrite(new JsonRpcResponse
        {
            Id = request.Id,
            Result = result,
        });
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        _messages.Writer.TryComplete();
        return ValueTask.CompletedTask;
    }

    private static JsonNode Discover() => new JsonObject
    {
        ["supportedVersions"] = new JsonArray("2026-07-28"),
        ["capabilities"] = new JsonObject
        {
            ["extensions"] = new JsonObject
            {
                ["io.modelcontextprotocol/tasks"] = new JsonObject(),
            },
        },
        ["ttlMs"] = 0,
        ["cacheScope"] = "private",
    };

    private static JsonNode CreateTask() => Serialize(
        new CreateTaskResult
        {
            TaskId = "task-1",
            Status = McpTaskStatus.Working,
            CreatedAt = FixedTime,
            LastUpdatedAt = FixedTime,
            PollIntervalMs = 1,
        },
        McpTasksJsonContext.Default.CreateTaskResult);

    private JsonNode GetTask()
    {
        GetCalls++;
        InputRequest input = InputRequest.ForElicitation(
            new ElicitRequestParams
            {
                Mode = "url",
                Message = "Approve the operation?",
                Url = "https://example.invalid/approve",
            });

        return Serialize(
            new InputRequiredTaskResult
            {
                TaskId = "task-1",
                CreatedAt = FixedTime,
                LastUpdatedAt = FixedTime,
                PollIntervalMs = 1,
                InputRequests = new Dictionary<string, InputRequest>
                {
                    ["approval"] = input,
                },
            },
            McpTasksJsonContext.Default.InputRequiredTaskResult);
    }

    private JsonNode UpdateTask(JsonRpcRequest request)
    {
        UpdateCalls++;
        using JsonDocument parameters = ParseParameters(request);
        JsonElement root = parameters.RootElement;
        UpdateEnvelopeValid =
            root.GetProperty("taskId").GetString() == "task-1" &&
            root.GetProperty("inputResponses")
                .GetProperty("approval")
                .GetProperty("action")
                .GetString() == "decline";

        return Serialize(
            new UpdateTaskResult(),
            McpTasksJsonContext.Default.UpdateTaskResult);
    }

    private JsonNode CancelTask(JsonRpcRequest request)
    {
        CancelCalls++;
        using JsonDocument parameters = ParseParameters(request);
        CancelEnvelopeValid =
            parameters.RootElement.GetProperty("taskId").GetString() == "task-1";

        return Serialize(
            new CancelTaskResult(),
            McpTasksJsonContext.Default.CancelTaskResult);
    }

    private static JsonDocument ParseParameters(JsonRpcRequest request) =>
        JsonDocument.Parse(
            request.Params?.ToString()
                ?? throw new InvalidOperationException("Request parameters were missing."));

    private static JsonNode Serialize<T>(
        T value,
        System.Text.Json.Serialization.Metadata.JsonTypeInfo<T> typeInfo) =>
        JsonSerializer.SerializeToNode(value, typeInfo)!;
}
