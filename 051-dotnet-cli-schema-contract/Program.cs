using System.Diagnostics;
using System.Text.Json.Nodes;

var schema = await CaptureSchemaAsync();
var checks = CreateChecks();
var failures = new List<string>();

Console.WriteLine($"Schema version: {StringValue(schema["version"])}");

foreach (var check in checks)
{
    if (check.Evaluate(schema))
    {
        Console.WriteLine($"PASS {check.Name}");
    }
    else
    {
        failures.Add(check.Name);
        Console.WriteLine($"FAIL {check.Name}");
    }
}

var driftedSchema = (JsonObject)schema.DeepClone();
var configurationAliases = GetOption(driftedSchema, "--configuration")?["aliases"] as JsonArray
    ?? throw new InvalidOperationException("The live schema has no --configuration aliases array.");

var shortAliasIndex = Enumerable.Range(0, configurationAliases.Count)
    .FirstOrDefault(index => StringValue(configurationAliases[index]) == "-c", -1);

if (shortAliasIndex < 0)
{
    throw new InvalidOperationException("The live schema has no -c alias to mutate.");
}

configurationAliases.RemoveAt(shortAliasIndex);
var driftFailures = checks
    .Where(check => !check.Evaluate(driftedSchema))
    .Select(check => check.Name)
    .ToArray();

const string aliasCheckName = "--configuration keeps the -c alias";
var negativeControlPassed = driftFailures is [aliasCheckName];

if (negativeControlPassed)
{
    Console.WriteLine("PASS negative control rejects a missing -c alias");
}
else
{
    failures.Add("negative control");
    Console.WriteLine($"FAIL negative control produced: {string.Join(", ", driftFailures)}");
}

var totalChecks = checks.Count + 1;
var passedChecks = totalChecks - failures.Count;
Console.WriteLine($"Verifier: {passedChecks}/{totalChecks} passed");

return failures.Count == 0 ? 0 : 1;

static async Task<JsonObject> CaptureSchemaAsync()
{
    var startInfo = new ProcessStartInfo("dotnet")
    {
        RedirectStandardOutput = true,
        RedirectStandardError = true,
        UseShellExecute = false,
        CreateNoWindow = true,
    };

    startInfo.ArgumentList.Add("build");
    startInfo.ArgumentList.Add("--cli-schema");
    startInfo.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
    startInfo.Environment["DOTNET_NOLOGO"] = "1";

    using var process = Process.Start(startInfo)
        ?? throw new InvalidOperationException("Could not start the dotnet CLI.");

    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
    var standardErrorTask = process.StandardError.ReadToEndAsync();

    await process.WaitForExitAsync();
    var standardOutput = await standardOutputTask;
    var standardError = await standardErrorTask;

    if (process.ExitCode != 0)
    {
        throw new InvalidOperationException(
            $"dotnet build --cli-schema exited with {process.ExitCode}: {standardError.Trim()}");
    }

    return JsonNode.Parse(standardOutput) as JsonObject
        ?? throw new InvalidOperationException("The CLI schema was not a JSON object.");
}

static List<ContractCheck> CreateChecks() =>
[
    new("command name is build", root => StringValue(root["name"]) == "build"),
    new("schema reports a .NET 10 SDK", root =>
        Version.TryParse(StringValue(root["version"]), out var version) && version.Major == 10),
    new("project-or-solution argument is present", root => GetProjectArgument(root) is not null),
    new("project-or-solution argument remains optional", root =>
        IntValue(GetProjectArgument(root)?["arity"]?["minimum"]) == 0),
    new("--configuration remains public", root =>
        BoolValue(GetOption(root, "--configuration")?["hidden"]) == false),
    new("--configuration keeps the -c alias", root =>
        HasAlias(GetOption(root, "--configuration"), "-c")),
    new("--configuration accepts one string", root =>
        StringValue(GetOption(root, "--configuration")?["valueType"]) == "System.String" &&
        HasArity(GetOption(root, "--configuration"), minimum: 1, maximum: 1)),
    new("--no-restore remains Boolean", root =>
        StringValue(GetOption(root, "--no-restore")?["valueType"]) == "System.Boolean"),
    new("--no-restore accepts no value", root =>
        HasArity(GetOption(root, "--no-restore"), minimum: 0, maximum: 0)),
    new("--framework keeps the -f alias", root =>
        HasAlias(GetOption(root, "--framework"), "-f")),
    new("--framework accepts one value", root =>
        HasArity(GetOption(root, "--framework"), minimum: 1, maximum: 1)),
];

static JsonObject? GetProjectArgument(JsonObject root)
{
    if (root["arguments"] is not JsonObject arguments)
    {
        return null;
    }

    return arguments
        .FirstOrDefault(pair =>
            pair.Key.Contains("PROJECT", StringComparison.Ordinal) &&
            pair.Key.Contains("SOLUTION", StringComparison.Ordinal))
        .Value as JsonObject;
}

static JsonObject? GetOption(JsonObject root, string name) =>
    root["options"] is JsonObject options ? options[name] as JsonObject : null;

static bool HasAlias(JsonObject? option, string expectedAlias) =>
    option?["aliases"] is JsonArray aliases &&
    aliases.Any(alias => StringValue(alias) == expectedAlias);

static bool HasArity(JsonObject? option, int minimum, int maximum) =>
    IntValue(option?["arity"]?["minimum"]) == minimum &&
    IntValue(option?["arity"]?["maximum"]) == maximum;

static string? StringValue(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<string>(out var result) ? result : null;

static int? IntValue(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<int>(out var result) ? result : null;

static bool? BoolValue(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<bool>(out var result) ? result : null;

internal sealed record ContractCheck(string Name, Func<JsonObject, bool> Predicate)
{
    public bool Evaluate(JsonObject root)
    {
        try
        {
            return Predicate(root);
        }
        catch (InvalidOperationException)
        {
            return false;
        }
    }
}
