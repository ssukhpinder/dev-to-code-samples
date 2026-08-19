using System.Text.Json;
using McpHeaderValidation;

const string ValidSchema = """
    {
      "type": "object",
      "properties": {
        "region": { "type": "string", "x-mcp-header": "Region" },
        "route": {
          "type": "object",
          "properties": {
            "tenantId": { "type": "integer", "x-mcp-header": "Tenant" }
          }
        },
        "dryRun": { "type": "boolean", "x-mcp-header": "Dry-Run" },
        "label": { "type": "string", "x-mcp-header": "Label" }
      }
    }
    """;

const string IntegerSchema = """
    {
      "type": "object",
      "properties": {
        "sequence": { "type": "integer", "x-mcp-header": "Sequence" }
      }
    }
    """;

List<(string Name, Action Check)> checks =
[
    ("projects nested primitive arguments", ProjectsNestedArguments),
    ("omits an absent optional argument", OmitsAbsentArgument),
    ("omits an explicit null argument", OmitsNullArgument),
    ("base64-encodes unsafe and sentinel values", EncodesUnsafeValues),
    ("rejects duplicate header names", RejectsDuplicateNames),
    ("rejects number annotations", RejectsNumberType),
    ("rejects unreachable annotations", RejectsUnreachableAnnotations),
    ("ignores annotation-shaped literal data", IgnoresLiteralData),
    ("rejects invalid HTTP tokens", RejectsInvalidToken),
    ("normalizes integral JSON number forms", NormalizesIntegerForms),
    ("rejects rounded near-integer values", RejectsNearInteger),
    ("accepts JavaScript-safe integer boundaries", AcceptsIntegerBoundaries),
    ("rejects integers outside both boundaries", RejectsUnsafeIntegers)
];

int passed = 0;

foreach ((string name, Action check) in checks)
{
    check();
    passed++;
    Console.WriteLine($"PASS: {name}");
}

Console.WriteLine($"{passed}/{checks.Count} checks passed");

static void ProjectsNestedArguments()
{
    IReadOnlyDictionary<string, string> headers = Project(ValidSchema, """
        {
          "region": "us-west1",
          "route": { "tenantId": 42 },
          "dryRun": true,
          "label": "Hello, 世界"
        }
        """);

    Equal("us-west1", headers["Mcp-Param-Region"]);
    Equal("42", headers["Mcp-Param-Tenant"]);
    Equal("true", headers["Mcp-Param-Dry-Run"]);
    Equal("=?base64?SGVsbG8sIOS4lueVjA==?=", headers["Mcp-Param-Label"]);
}

static void OmitsAbsentArgument()
{
    IReadOnlyDictionary<string, string> headers = Project(ValidSchema, """
        {
          "region": "eu-west1",
          "route": { "tenantId": 7 },
          "dryRun": false
        }
        """);

    Equal(3, headers.Count);
    Equal(false, headers.ContainsKey("Mcp-Param-Label"));
}

static void OmitsNullArgument()
{
    IReadOnlyDictionary<string, string> headers = Project(ValidSchema, """
        {
          "region": "eu-west1",
          "route": { "tenantId": 7 },
          "dryRun": false,
          "label": null
        }
        """);

    Equal(3, headers.Count);
    Equal(false, headers.ContainsKey("Mcp-Param-Label"));
}

static void EncodesUnsafeValues()
{
    Equal("=?base64?IHBhZGRlZCA=?=", McpHeaderProjector.EncodeHeaderValue(" padded "));
    Equal(
        "=?base64?PT9iYXNlNjQ/bGl0ZXJhbD89?=",
        McpHeaderProjector.EncodeHeaderValue("=?base64?literal?="));
}

static void RejectsDuplicateNames() => ExpectInvalidSchema("""
    {
      "type": "object",
      "properties": {
        "tenant": { "type": "string", "x-mcp-header": "Tenant" },
        "backup": { "type": "string", "x-mcp-header": "tenant" }
      }
    }
    """, "case-insensitively unique");

static void RejectsNumberType() => ExpectInvalidSchema("""
    {
      "type": "object",
      "properties": {
        "ratio": { "type": "number", "x-mcp-header": "Ratio" }
      }
    }
    """, "string, integer, or boolean");

static void RejectsUnreachableAnnotations()
{
    ExpectInvalidSchema("""
        {
          "type": "object",
          "properties": {
            "filters": {
              "type": "array",
              "items": { "type": "string", "x-mcp-header": "Filter" }
            }
          }
        }
        """, "statically reachable");

    ExpectInvalidSchema("""
        {
          "type": "object",
          "properties": {
            "region": {
              "oneOf": [
                { "type": "string", "x-mcp-header": "Region" }
              ]
            }
          }
        }
        """, "statically reachable");

    ExpectInvalidSchema("""
        {
          "$schema": "http://json-schema.org/draft-07/schema#",
          "type": "object",
          "dependencies": {
            "region": {
              "properties": {
                "backup": { "type": "string", "x-mcp-header": "Backup" }
              }
            }
          }
        }
        """, "statically reachable");
}

static void IgnoresLiteralData()
{
    IReadOnlyDictionary<string, string> headers = Project("""
        {
          "type": "object",
          "dependencies": {
            "region": ["backup"]
          },
          "properties": {
            "region": {
              "type": "string",
              "x-mcp-header": "Region",
              "default": { "x-mcp-header": "ordinary data" },
              "examples": [{ "x-mcp-header": "more ordinary data" }]
            }
          }
        }
        """, """{ "region": "ca-central1" }""");

    Equal("ca-central1", headers["Mcp-Param-Region"]);
}

static void RejectsInvalidToken() => ExpectInvalidSchema("""
    {
      "type": "object",
      "properties": {
        "tenant": { "type": "string", "x-mcp-header": "Tenant\r\nInjected" }
      }
    }
    """, "HTTP token");

static void NormalizesIntegerForms()
{
    Equal("42", Project(IntegerSchema, """{ "sequence": 42.0 }""")["Mcp-Param-Sequence"]);
    Equal("42", Project(IntegerSchema, """{ "sequence": 4.2e1 }""")["Mcp-Param-Sequence"]);
}

static void RejectsNearInteger()
{
    InvalidOperationException exception = ExpectThrows<InvalidOperationException>(() =>
        Project(IntegerSchema, """{ "sequence": 42.00000000000000000000000000001 }"""));

    Contains(exception.Message, "valid integer");
}

static void AcceptsIntegerBoundaries()
{
    Equal(
        "9007199254740991",
        Project(IntegerSchema, """{ "sequence": 9007199254740991 }""")["Mcp-Param-Sequence"]);
    Equal(
        "-9007199254740991",
        Project(IntegerSchema, """{ "sequence": -9007199254740991 }""")["Mcp-Param-Sequence"]);
}

static void RejectsUnsafeIntegers()
{
    InvalidOperationException positive = ExpectThrows<InvalidOperationException>(() =>
        Project(IntegerSchema, """{ "sequence": 9007199254740992 }"""));
    InvalidOperationException negative = ExpectThrows<InvalidOperationException>(() =>
        Project(IntegerSchema, """{ "sequence": -9007199254740992 }"""));

    Contains(positive.Message, "valid integer");
    Contains(negative.Message, "valid integer");
}

static IReadOnlyDictionary<string, string> Project(string schemaJson, string argumentJson)
{
    using JsonDocument schema = JsonDocument.Parse(schemaJson);
    using JsonDocument arguments = JsonDocument.Parse(argumentJson);
    return McpHeaderProjector.Project(schema.RootElement, arguments.RootElement);
}

static void ExpectInvalidSchema(string schemaJson, string expectedMessage)
{
    InvalidOperationException exception = ExpectThrows<InvalidOperationException>(() =>
        Project(schemaJson, "{}"));
    Contains(exception.Message, expectedMessage);
}

static TException ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static void Equal<T>(T expected, T actual)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"Expected '{expected}', got '{actual}'.");
    }
}

static void Contains(string actual, string expected)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"Expected '{actual}' to contain '{expected}'.");
    }
}
