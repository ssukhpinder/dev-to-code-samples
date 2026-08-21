using System.Text.Json;
using System.Text.Json.Serialization;

var manager = new Employee { Name = "Mina" };
var report = new Employee { Name = "Iris", Manager = manager };
manager.DirectReports.Add(report);

var passed = 0;

void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Verification failed: {name}");
    }

    passed++;
    Console.WriteLine($"PASS {passed}: {name}");
}

var defaultContextRejectedCycle = false;
try
{
    _ = JsonSerializer.Serialize(manager, DefaultGraphContext.Default.Employee);
}
catch (Exception exception) when (exception is JsonException or InvalidOperationException)
{
    defaultContextRejectedCycle = true;
}

Check(defaultContextRejectedCycle, "the default generated context rejects the cycle");
Check(
    PreserveGraphContext.Default.Options.ReferenceHandler == ReferenceHandler.Preserve,
    "the generated context is configured with ReferenceHandler.Preserve");
Check(!JsonSerializer.IsReflectionEnabledByDefault, "reflection fallback is disabled");

var json = JsonSerializer.Serialize(manager, PreserveGraphContext.Default.Employee);

Check(json.Contains("\"$id\"", StringComparison.Ordinal), "the payload contains object identifiers");
Check(json.Contains("\"$ref\"", StringComparison.Ordinal), "the payload contains a back-reference");

var roundTrip = JsonSerializer.Deserialize(json, PreserveGraphContext.Default.Employee);

Check(roundTrip is not null, "the payload deserializes");
Check(roundTrip!.DirectReports.Count == 1, "the report collection round-trips");
Check(
    ReferenceEquals(roundTrip, roundTrip.DirectReports[0].Manager),
    "the manager/report cycle keeps object identity");

Console.WriteLine();
Console.WriteLine(json);
Console.WriteLine();
Console.WriteLine($"Verified {passed}/8 checks.");

internal sealed class Employee
{
    public string Name { get; set; } = string.Empty;

    public Employee? Manager { get; set; }

    public List<Employee> DirectReports { get; set; } = [];
}

[JsonSerializable(typeof(Employee))]
internal partial class DefaultGraphContext : JsonSerializerContext;

[JsonSourceGenerationOptions(
    ReferenceHandler = JsonKnownReferenceHandler.Preserve,
    WriteIndented = true)]
[JsonSerializable(typeof(Employee))]
internal partial class PreserveGraphContext : JsonSerializerContext;
