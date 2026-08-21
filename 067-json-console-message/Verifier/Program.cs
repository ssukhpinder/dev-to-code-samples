using System.Diagnostics;
using System.Text.Json;

const string ExpectedMessage = "Order 42 moved to ready.";
const string ExpectedTemplate = "Order {OrderId} moved to {Status}.";

var checks = 0;
var sampleRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var fixturePath = Path.Combine(sampleRoot, "Fixtures", "net9-duplicated-message.json");
var emitterPath = Path.Combine(sampleRoot, "Emitter", "bin", "Release", "net10.0", "Emitter.dll");

using var legacyDocument = JsonDocument.Parse(await File.ReadAllTextAsync(fixturePath));
var legacy = legacyDocument.RootElement;

Check(ReadTopLevelMessage(legacy) == ExpectedMessage, "legacy fixture has a top-level Message");
Check(ReadStateMessage(legacy) == ExpectedMessage, "legacy fixture duplicates Message in State");
Check(ReadLegacyOnly(legacy) == ExpectedMessage, "legacy-only parser reads the legacy fixture");

var currentJson = await EmitCurrentLogAsync(emitterPath);
using var currentDocument = JsonDocument.Parse(currentJson);
var current = currentDocument.RootElement;

Check(ReadTopLevelMessage(current) == ExpectedMessage, ".NET 10 emits the top-level Message");
Check(ReadStateMessage(current) is null, ".NET 10 omits the duplicate State.Message");
Check(ReadLegacyOnly(current) is null, "legacy-only parser misses the .NET 10 message");
Check(ReadCompatible(current) == ExpectedMessage, "compatible parser reads the .NET 10 message");
Check(ReadCompatible(legacy) == ExpectedMessage, "compatible parser still reads the legacy fixture");

var state = current.GetProperty("State");
Check(state.GetProperty("OrderId").GetInt32() == 42, "structured OrderId remains in State");
Check(state.GetProperty("Status").GetString() == "ready", "structured Status remains in State");
Check(state.GetProperty("{OriginalFormat}").GetString() == ExpectedTemplate, "original format remains in State");

Console.WriteLine($"PASS: {checks}/11 checks");

static string? ReadCompatible(JsonElement root) =>
    ReadTopLevelMessage(root) ?? ReadStateMessage(root);

static string? ReadLegacyOnly(JsonElement root) => ReadStateMessage(root);

static string? ReadTopLevelMessage(JsonElement root) =>
    root.TryGetProperty("Message", out var message) && message.ValueKind == JsonValueKind.String
        ? message.GetString()
        : null;

static string? ReadStateMessage(JsonElement root) =>
    root.TryGetProperty("State", out var state) &&
    state.ValueKind == JsonValueKind.Object &&
    state.TryGetProperty("Message", out var message) &&
    message.ValueKind == JsonValueKind.String
        ? message.GetString()
        : null;

static async Task<string> EmitCurrentLogAsync(string emitterPath)
{
    if (!File.Exists(emitterPath))
    {
        throw new FileNotFoundException("Build the solution before running the verifier.", emitterPath);
    }

    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
    };
    startInfo.ArgumentList.Add(emitterPath);

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the JSON log emitter.");

    var stdoutTask = process.StandardOutput.ReadToEndAsync();
    var stderrTask = process.StandardError.ReadToEndAsync();
    await process.WaitForExitAsync();
    var stdout = await stdoutTask;
    var stderr = await stderrTask;

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException($"Emitter exited {process.ExitCode}: {stderr}");
    }

    var lines = stdout.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries);
    if (lines.Length != 1)
    {
        throw new InvalidOperationException($"Expected one JSON log line, received {lines.Length}.");
    }

    return lines[0];
}

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}
