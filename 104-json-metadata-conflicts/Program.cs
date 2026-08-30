using System.Text.Json;
using System.Text.Json.Serialization;

var runtimeMajor = Environment.Version.Major;
if (runtimeMajor is not (9 or 10))
{
    Console.Error.WriteLine($"This comparison expects .NET 9 or .NET 10, not .NET {runtimeMajor}.");
    return 2;
}

string? brokenJson = null;
var brokenSerialization = CaptureException(
    () => brokenJson = JsonSerializer.Serialize<BrokenEvent>(
        new BrokenCreatedEvent { Id = "evt-104" }));

var brokenTypePropertyCount = brokenJson is null
    ? 0
    : CountProperties(brokenJson, "Type");
var brokenRoundTrip = brokenJson is null
    ? "not-run"
    : CaptureException(() => JsonSerializer.Deserialize<BrokenEvent>(brokenJson));

var fixedJson = JsonSerializer.Serialize<FixedEvent>(
    new FixedCreatedEvent { Id = "evt-104" });
var fixedRoundTrip = JsonSerializer.Deserialize<FixedEvent>(fixedJson);

var expectedBrokenSerialization = runtimeMajor == 9 ? "none" : nameof(InvalidOperationException);
var expectedBrokenTypePropertyCount = runtimeMajor == 9 ? 2 : 0;
var expectedBrokenRoundTrip = runtimeMajor == 9 ? nameof(JsonException) : "not-run";

var checks = new[]
{
    new Check("runtime", runtimeMajor is 9 or 10),
    new Check("broken-serialization", brokenSerialization == expectedBrokenSerialization),
    new Check("broken-type-property-count", brokenTypePropertyCount == expectedBrokenTypePropertyCount),
    new Check("broken-round-trip", brokenRoundTrip == expectedBrokenRoundTrip),
    new Check("fixed-discriminator", GetSinglePropertyValue(fixedJson, "$kind") == "created"),
    new Check("fixed-domain-type", GetSinglePropertyValue(fixedJson, "Type") == "Created"),
    new Check(
        "fixed-round-trip",
        fixedRoundTrip is FixedCreatedEvent { Id: "evt-104", Type: "Created" }),
};

Console.WriteLine($"runtime-major={runtimeMajor}");
Console.WriteLine($"broken-serialize={brokenSerialization}");
Console.WriteLine($"broken-type-property-count={brokenTypePropertyCount}");
Console.WriteLine($"broken-roundtrip={brokenRoundTrip}");
Console.WriteLine($"fixed-kind={GetSinglePropertyValue(fixedJson, "$kind")}");
Console.WriteLine($"fixed-type={GetSinglePropertyValue(fixedJson, "Type")}");
Console.WriteLine($"fixed-roundtrip={fixedRoundTrip?.GetType().Name ?? "null"}");

var failures = checks.Where(check => !check.Passed).ToArray();
if (failures.Length != 0)
{
    foreach (var failure in failures)
    {
        Console.Error.WriteLine($"FAIL: {failure.Name}");
    }

    return 1;
}

Console.WriteLine($"PASS: {checks.Length}/{checks.Length}");
return 0;

static string CaptureException(Action action)
{
    try
    {
        action();
        return "none";
    }
    catch (Exception exception)
    {
        return exception.GetType().Name;
    }
}

static int CountProperties(string json, string name)
{
    using var document = JsonDocument.Parse(json);
    return document.RootElement
        .EnumerateObject()
        .Count(property => property.NameEquals(name));
}

static string GetSinglePropertyValue(string json, string name)
{
    using var document = JsonDocument.Parse(json);
    var matches = document.RootElement
        .EnumerateObject()
        .Where(property => property.NameEquals(name))
        .Select(property => property.Value.GetString())
        .ToArray();

    return matches.Length == 1 ? matches[0] ?? "null" : $"count:{matches.Length}";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "Type")]
[JsonDerivedType(typeof(BrokenCreatedEvent), "created")]
internal abstract class BrokenEvent
{
    public required string Id { get; init; }

    public abstract string Type { get; }
}

internal sealed class BrokenCreatedEvent : BrokenEvent
{
    public override string Type => "Created";
}

[JsonPolymorphic(TypeDiscriminatorPropertyName = "$kind")]
[JsonDerivedType(typeof(FixedCreatedEvent), "created")]
internal abstract class FixedEvent
{
    public required string Id { get; init; }

    public abstract string Type { get; }
}

internal sealed class FixedCreatedEvent : FixedEvent
{
    public override string Type => "Created";
}

internal sealed record Check(string Name, bool Passed);
