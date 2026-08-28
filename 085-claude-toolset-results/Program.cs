using System.Text.Json;
using System.Text.Json.Serialization;

const string modelTurnJson = """
{
  "stop_reason": "tool_use",
  "content": [
    {
      "type": "tool_use",
      "id": "toolu_browser_01",
      "name": "screenshot",
      "toolset_name": "browser",
      "input": {}
    },
    {
      "type": "tool_use",
      "id": "toolu_custom_01",
      "name": "screenshot",
      "input": { "target": "receipt" }
    }
  ]
}
""";

const string placeholderPng =
    "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

var checks = new CheckRunner();
var turn = JsonSerializer.Deserialize<ModelTurn>(modelTurnJson)
    ?? throw new InvalidOperationException("The model-turn fixture did not deserialize.");

checks.Expect("fixture stops for tool use", turn.StopReason == "tool_use");
checks.Expect(
    "fixture contains two screenshot calls",
    turn.Content is
    [
    { Name: "screenshot", ToolsetName: "browser" },
    { Name: "screenshot", ToolsetName: null }
    ]);

var dispatcher = new ToolDispatcher(new Dictionary<ToolKey, Func<JsonElement, JsonElement>>
{
    [new("browser", "screenshot")] = _ => ResultContent.Png(placeholderPng),
    [new(null, "screenshot")] = input =>
        ResultContent.Text(
            $"custom screenshot captured for {input.GetProperty("target").GetString()}")
});

var results = dispatcher.Execute(turn.Content);

checks.Expect(
    "browser member returns the required PNG image block",
    results[0].Content.ValueKind == JsonValueKind.Array
    && results[0].Content.GetArrayLength() == 1
    && results[0].Content[0].GetProperty("type").GetString() == "image"
    && results[0].Content[0].GetProperty("source").GetProperty("type").GetString() == "base64"
    && results[0].Content[0].GetProperty("source").GetProperty("media_type").GetString() == "image/png"
    && results[0].Content[0].GetProperty("source").GetProperty("data").GetString() == placeholderPng);
checks.Expect(
    "same-named custom tool uses the custom handler",
    results[1].Content.ValueKind == JsonValueKind.String
    && results[1].Content.GetString() == "custom screenshot captured for receipt");
checks.Expect(
    "browser result echoes toolset_name",
    results[0].ToolsetName == "browser");
checks.Expect(
    "custom result omits toolset_name",
    results[1].ToolsetName is null);
checks.Expect(
    "correct results pass correlation preflight",
    ToolResultContract.Validate(turn.Content, results).Count == 0);

var omittedToolset = results
    .Select(result => result.ToolUseId == "toolu_browser_01"
        ? result with { ToolsetName = null }
        : result)
    .ToArray();
var omittedErrors = ToolResultContract.Validate(turn.Content, omittedToolset);
checks.Expect(
    "omitted browser toolset_name fails preflight",
    omittedErrors is ["toolu_browser_01 must echo toolset_name 'browser'."]);

var mismatchedToolset = results
    .Select(result => result.ToolUseId == "toolu_browser_01"
        ? result with { ToolsetName = "computer" }
        : result)
    .ToArray();
var mismatchErrors = ToolResultContract.Validate(turn.Content, mismatchedToolset);
checks.Expect(
    "mismatched member toolset_name fails preflight",
    mismatchErrors is ["toolu_browser_01 must echo toolset_name 'browser', not 'computer'."]);

var serializedResults = JsonSerializer.Serialize(results, SerializerOptions.Indented);
using var resultDocument = JsonDocument.Parse(serializedResults);
var resultElements = resultDocument.RootElement;
checks.Expect(
    "serialized result preserves image content and member-only correlation",
    resultElements[0].GetProperty("toolset_name").GetString() == "browser"
    && resultElements[0].GetProperty("content")[0].GetProperty("type").GetString() == "image"
    && !resultElements[1].TryGetProperty("toolset_name", out _));

Console.WriteLine();
Console.WriteLine("Next user-message content:");
Console.WriteLine(serializedResults);
Console.WriteLine();
Console.WriteLine($"Summary: {checks.Passed}/{checks.Total} checks passed.");

internal sealed record ModelTurn(
    [property: JsonPropertyName("stop_reason")] string StopReason,
    [property: JsonPropertyName("content")] ToolUseBlock[] Content);

internal sealed record ToolUseBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("toolset_name")] string? ToolsetName,
    [property: JsonPropertyName("input")] JsonElement Input);

internal sealed record ToolResultBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("tool_use_id")] string ToolUseId,
    [property: JsonPropertyName("content")] JsonElement Content,
    [property: JsonPropertyName("toolset_name")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    string? ToolsetName);

internal readonly record struct ToolKey(string? ToolsetName, string Name);

internal sealed class ToolDispatcher(
    IReadOnlyDictionary<ToolKey, Func<JsonElement, JsonElement>> handlers)
{
    public ToolResultBlock[] Execute(IEnumerable<ToolUseBlock> calls) =>
        calls.Select(Execute).ToArray();

    private ToolResultBlock Execute(ToolUseBlock call)
    {
        var key = new ToolKey(call.ToolsetName, call.Name);
        if (!handlers.TryGetValue(key, out var handler))
        {
            throw new InvalidOperationException(
                $"No handler is registered for ({call.ToolsetName ?? "custom"}, {call.Name}).");
        }

        return new ToolResultBlock(
            Type: "tool_result",
            ToolUseId: call.Id,
            Content: handler(call.Input),
            ToolsetName: call.ToolsetName);
    }
}

internal static class ResultContent
{
    public static JsonElement Text(string value) =>
        JsonSerializer.SerializeToElement(value);

    public static JsonElement Png(string base64Data) =>
        JsonSerializer.SerializeToElement(
            new[]
            {
                new ImageContentBlock(
                    Type: "image",
                    Source: new ImageSource(
                        Type: "base64",
                        MediaType: "image/png",
                        Data: base64Data))
            });
}

internal sealed record ImageContentBlock(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("source")] ImageSource Source);

internal sealed record ImageSource(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("media_type")] string MediaType,
    [property: JsonPropertyName("data")] string Data);

internal static class ToolResultContract
{
    public static IReadOnlyList<string> Validate(
        IReadOnlyCollection<ToolUseBlock> calls,
        IReadOnlyCollection<ToolResultBlock> results)
    {
        var errors = new List<string>();
        var callsById = calls.ToDictionary(call => call.Id, StringComparer.Ordinal);
        var resultsById = results
            .GroupBy(result => result.ToolUseId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.Ordinal);

        foreach (var call in calls)
        {
            if (!resultsById.TryGetValue(call.Id, out var matches) || matches.Length != 1)
            {
                errors.Add($"{call.Id} must have exactly one tool_result.");
                continue;
            }

            var result = matches[0];
            if (call.ToolsetName is not null && result.ToolsetName != call.ToolsetName)
            {
                var suffix = result.ToolsetName is null
                    ? "."
                    : $", not '{result.ToolsetName}'.";
                errors.Add($"{call.Id} must echo toolset_name '{call.ToolsetName}'{suffix}");
            }
            else if (call.ToolsetName is null && result.ToolsetName is not null)
            {
                errors.Add($"{call.Id} is a custom tool and must omit toolset_name.");
            }
        }

        foreach (var result in results.Where(result => !callsById.ContainsKey(result.ToolUseId)))
        {
            errors.Add($"{result.ToolUseId} does not match a tool_use block.");
        }

        return errors;
    }
}

internal sealed class CheckRunner
{
    public int Passed { get; private set; }

    public int Total { get; private set; }

    public void Expect(string description, bool condition)
    {
        Total++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL {Total}: {description}");
        }

        Passed++;
        Console.WriteLine($"PASS {Total}: {description}");
    }
}

internal static class SerializerOptions
{
    public static readonly JsonSerializerOptions Indented = new()
    {
        WriteIndented = true
    };
}
