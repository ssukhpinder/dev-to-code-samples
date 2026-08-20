using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

var catalogAJson = """
    [
      {
        "name": "read_invoice",
        "description": "Read one invoice by ID.",
        "inputSchema": {
          "type": "object",
          "properties": { "invoiceId": { "type": "string" } },
          "required": ["invoiceId"],
          "additionalProperties": false
        }
      },
      {
        "name": "get_weather",
        "title": "Current weather",
        "description": "Read the current weather for a city.",
        "inputSchema": {
          "type": "object",
          "properties": {
            "city": { "type": "string" },
            "units": { "enum": ["celsius", "fahrenheit"] }
          },
          "required": ["city"],
          "additionalProperties": false
        },
        "annotations": { "readOnlyHint": true }
      },
      {
        "name": "export_report",
        "description": "Export a report as CSV.",
        "inputSchema": {
          "type": "object",
          "properties": { "reportId": { "type": "string" } },
          "required": ["reportId"],
          "additionalProperties": false
        }
      }
    ]
    """;

// Same catalog, but tool registration order and JSON object-property order differ.
var catalogBJson = """
    [
      {
        "annotations": { "readOnlyHint": true },
        "inputSchema": {
          "additionalProperties": false,
          "required": ["city"],
          "properties": {
            "units": { "enum": ["celsius", "fahrenheit"] },
            "city": { "type": "string" }
          },
          "type": "object"
        },
        "description": "Read the current weather for a city.",
        "title": "Current weather",
        "name": "get_weather"
      },
      {
        "inputSchema": {
          "required": ["reportId"],
          "additionalProperties": false,
          "properties": { "reportId": { "type": "string" } },
          "type": "object"
        },
        "description": "Export a report as CSV.",
        "name": "export_report"
      },
      {
        "inputSchema": {
          "properties": { "invoiceId": { "type": "string" } },
          "additionalProperties": false,
          "type": "object",
          "required": ["invoiceId"]
        },
        "description": "Read one invoice by ID.",
        "name": "read_invoice"
      }
    ]
    """;

var verifier = new Verifier();

verifier.Check(
    !ReadToolNames(catalogAJson).SequenceEqual(ReadToolNames(catalogBJson), StringComparer.Ordinal),
    "registration order differs before canonicalization");

var stableA = ToolCatalogCanonicalizer.Create(catalogAJson);
var stableB = ToolCatalogCanonicalizer.Create(catalogBJson);
verifier.Check(stableA.CanonicalJson == stableB.CanonicalJson, "canonical JSON ignores registration and object-key order");
verifier.Check(stableA.Fingerprint == stableB.Fingerprint, "equivalent catalogs have the same fingerprint");
verifier.SequenceEqual(
    ["export_report", "get_weather", "read_invoice"],
    stableA.ToolNames,
    "tool names use ordinal ordering");

using (var canonicalDocument = JsonDocument.Parse(stableA.CanonicalJson))
{
    var weatherSchema = canonicalDocument.RootElement
        .EnumerateArray()
        .Single(tool => tool.GetProperty("name").GetString() == "get_weather")
        .GetProperty("inputSchema");

    verifier.SequenceEqual(
        ["additionalProperties", "properties", "required", "type"],
        weatherSchema.EnumerateObject().Select(property => property.Name),
        "schema object keys are recursively sorted");
    verifier.SequenceEqual(
        ["city", "units"],
        weatherSchema.GetProperty("properties").EnumerateObject().Select(property => property.Name),
        "nested schema property keys are sorted");
}

verifier.Throws<InvalidDataException>(
    () => ToolCatalogCanonicalizer.Create(SelectTools(catalogAJson, "read_invoice", "read_invoice")),
    "duplicate tool names fail fast");
verifier.Throws<InvalidDataException>(
    () => ToolCatalogCanonicalizer.Create("""
        [
          {
            "name": "ambiguous_tool",
            "name": "different_tool",
            "inputSchema": { "type": "object" }
          }
        ]
        """),
    "duplicate JSON member names fail fast");

var changedMetadata = catalogAJson.Replace(
    "Read the current weather for a city.",
    "Read current weather and forecast data for a city.",
    StringComparison.Ordinal);
verifier.Check(
    stableA.Fingerprint != ToolCatalogCanonicalizer.Create(changedMetadata).Fingerprint,
    "metadata changes alter the fingerprint");

var changedSchema = catalogAJson.Replace(
    "\"required\": [\"city\"]",
    "\"required\": [\"city\", \"units\"]",
    StringComparison.Ordinal);
verifier.Check(
    stableA.Fingerprint != ToolCatalogCanonicalizer.Create(changedSchema).Fingerprint,
    "schema changes alter the fingerprint");

var readOnlyScopeA = ToolCatalogCanonicalizer.Create(SelectTools(catalogAJson, "get_weather", "read_invoice"));
var readOnlyScopeB = ToolCatalogCanonicalizer.Create(SelectTools(catalogBJson, "read_invoice", "get_weather"));
verifier.Check(
    readOnlyScopeA.Fingerprint == readOnlyScopeB.Fingerprint,
    "the same authorization-scoped set stays stable across registration order");
verifier.Check(
    stableA.Fingerprint != readOnlyScopeA.Fingerprint,
    "a legitimately different authorization-scoped set gets a different fingerprint");
verifier.Check(
    stableA.Fingerprint.Length == 64 && stableA.Fingerprint.All(IsLowerHex),
    "fingerprint is a lowercase SHA-256 hex value");

Console.WriteLine($"Catalog fingerprint: {stableA.Fingerprint}");
verifier.Complete();

static IReadOnlyList<string> ReadToolNames(string catalogJson)
{
    using var document = JsonDocument.Parse(catalogJson);
    return document.RootElement
        .EnumerateArray()
        .Select(tool => tool.GetProperty("name").GetString() ?? string.Empty)
        .ToArray();
}

static string SelectTools(string catalogJson, params string[] names)
{
    using var document = JsonDocument.Parse(catalogJson);
    var tools = document.RootElement
        .EnumerateArray()
        .ToDictionary(
            tool => tool.GetProperty("name").GetString() ?? throw new InvalidDataException("Tool name is null."),
            tool => tool,
            StringComparer.Ordinal);

    return $"[{string.Join(',', names.Select(name => tools[name].GetRawText()))}]";
}

static bool IsLowerHex(char character) =>
    character is >= '0' and <= '9' or >= 'a' and <= 'f';

internal sealed record ToolCatalogSnapshot(
    string CanonicalJson,
    string Fingerprint,
    IReadOnlyList<string> ToolNames);

internal static class ToolCatalogCanonicalizer
{
    private static readonly JsonDocumentOptions DocumentOptions = new()
    {
        AllowTrailingCommas = false,
        CommentHandling = JsonCommentHandling.Disallow,
        MaxDepth = 64,
    };

    public static ToolCatalogSnapshot Create(string toolsJson)
    {
        using var document = JsonDocument.Parse(toolsJson, DocumentOptions);
        if (document.RootElement.ValueKind != JsonValueKind.Array)
        {
            throw new InvalidDataException("The assembled MCP tools value must be a JSON array.");
        }

        var tools = new List<(string Name, JsonElement Value)>();
        var toolNames = new HashSet<string>(StringComparer.Ordinal);
        var index = 0;

        foreach (var tool in document.RootElement.EnumerateArray())
        {
            if (tool.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidDataException($"Tool at index {index} must be a JSON object.");
            }

            ValidateNoDuplicateMembers(tool, $"tools[{index}]");

            if (!tool.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String)
            {
                throw new InvalidDataException($"Tool at index {index} must have a string name.");
            }

            var name = nameElement.GetString();
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new InvalidDataException($"Tool at index {index} has an empty name.");
            }

            if (!toolNames.Add(name))
            {
                throw new InvalidDataException($"Duplicate MCP tool name: {name}");
            }

            tools.Add((name, tool));
            index++;
        }

        var ordered = tools.OrderBy(tool => tool.Name, StringComparer.Ordinal).ToArray();
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartArray();
            foreach (var tool in ordered)
            {
                WriteCanonicalJson(writer, tool.Value);
            }

            writer.WriteEndArray();
        }

        var bytes = stream.ToArray();
        var canonicalJson = Encoding.UTF8.GetString(bytes);
        var fingerprint = Convert.ToHexStringLower(SHA256.HashData(bytes));
        return new ToolCatalogSnapshot(canonicalJson, fingerprint, ordered.Select(tool => tool.Name).ToArray());
    }

    private static void ValidateNoDuplicateMembers(JsonElement element, string path)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in element.EnumerateObject())
                {
                    if (!names.Add(property.Name))
                    {
                        throw new InvalidDataException($"Duplicate JSON member '{property.Name}' at {path}.");
                    }

                    ValidateNoDuplicateMembers(property.Value, $"{path}.{property.Name}");
                }

                break;

            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    ValidateNoDuplicateMembers(item, $"{path}[{index}]");
                    index++;
                }

                break;
        }
    }

    private static void WriteCanonicalJson(Utf8JsonWriter writer, JsonElement element)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(property => property.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonicalJson(writer, property.Value);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonicalJson(writer, item);
                }

                writer.WriteEndArray();
                break;

            case JsonValueKind.String:
                writer.WriteStringValue(element.GetString());
                break;

            case JsonValueKind.Number:
                writer.WriteRawValue(element.GetRawText(), skipInputValidation: false);
                break;

            case JsonValueKind.True:
                writer.WriteBooleanValue(true);
                break;

            case JsonValueKind.False:
                writer.WriteBooleanValue(false);
                break;

            case JsonValueKind.Null:
                writer.WriteNullValue();
                break;

            default:
                throw new InvalidDataException($"Unsupported JSON value kind: {element.ValueKind}");
        }
    }
}

internal sealed class Verifier
{
    private int _passed;
    private int _total;

    public void Check(bool condition, string description)
    {
        _total++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {description}");
        }

        _passed++;
        Console.WriteLine($"PASS: {description}");
    }

    public void SequenceEqual(IEnumerable<string> expected, IEnumerable<string> actual, string description) =>
        Check(expected.SequenceEqual(actual, StringComparer.Ordinal), description);

    public void Throws<TException>(Action action, string description)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            Check(true, description);
            return;
        }

        Check(false, description);
    }

    public void Complete()
    {
        Console.WriteLine($"{_passed}/{_total} checks passed.");
        if (_passed != _total)
        {
            Environment.ExitCode = 1;
        }
    }
}
