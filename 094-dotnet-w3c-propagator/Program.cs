using System.Diagnostics;

const string incomingTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
const string expectedTraceId = "4bf92f3577b34da6a3ce929d0e0e4736";

Activity.DefaultIdFormat = ActivityIdFormat.W3C;
Activity.ForceDefaultIdFormat = true;

using var activity = new Activity("outbound")
    .SetParentId(incomingTraceParent)
    .AddBaggage("tenant.id", "north")
    .Start();

var currentHeaders = Inject(DistributedContextPropagator.Current, activity);
var defaultHeaders = Inject(DistributedContextPropagator.CreateDefaultPropagator(), activity);
var w3cHeaders = Inject(DistributedContextPropagator.CreateW3CPropagator(), activity);
var legacyHeaders = Inject(DistributedContextPropagator.CreatePreW3CPropagator(), activity);

var validW3cCarrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["traceparent"] = incomingTraceParent,
    ["tracestate"] = "vendor=opaque",
};

var legacyCarrier = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
{
    ["Request-Id"] = "|legacy.1.",
    ["Correlation-Context"] = "tenant.id=north",
};

var defaultParent = ExtractParent(
    DistributedContextPropagator.CreateDefaultPropagator(),
    validW3cCarrier);
var rejectedLegacyParent = ExtractParent(
    DistributedContextPropagator.CreateDefaultPropagator(),
    legacyCarrier);
var acceptedLegacyParent = ExtractParent(
    DistributedContextPropagator.CreatePreW3CPropagator(),
    legacyCarrier);
var compatibleInboundBaggage = DistributedContextPropagator
    .CreateDefaultPropagator()
    .ExtractBaggage(legacyCarrier, GetHeader) ?? [];

var checks = new (string Message, bool Passed)[]
{
    (
        "The process default matches the explicit W3C propagator",
        HeadersEqual(currentHeaders, w3cHeaders) && HeadersEqual(defaultHeaders, w3cHeaders)),
    (
        "The W3C traceparent keeps the upstream trace ID",
        currentHeaders.TryGetValue("traceparent", out var emittedParent)
        && emittedParent.StartsWith($"00-{expectedTraceId}-", StringComparison.Ordinal)
        && emittedParent.Length == 55),
    (
        "The .NET 10 default emits baggage, not Correlation-Context",
        currentHeaders.TryGetValue("baggage", out var baggage)
        && baggage.Contains("tenant.id", StringComparison.Ordinal)
        && baggage.Contains("north", StringComparison.Ordinal)
        && !currentHeaders.ContainsKey("Correlation-Context")),
    (
        "The pre-W3C propagator emits Correlation-Context, not baggage",
        legacyHeaders.TryGetValue("Correlation-Context", out var legacyBaggage)
        && legacyBaggage == "tenant.id=north"
        && !legacyHeaders.ContainsKey("baggage")),
    (
        "The default extracts a valid W3C parent and tracestate",
        defaultParent.TraceParent == incomingTraceParent
        && defaultParent.TraceState == "vendor=opaque"),
    (
        "The default rejects a hierarchical Request-Id parent",
        rejectedLegacyParent.TraceParent is null),
    (
        "The pre-W3C propagator accepts a hierarchical Request-Id parent",
        acceptedLegacyParent.TraceParent == "|legacy.1."),
    (
        "The default still reads legacy inbound baggage during migration",
        compatibleInboundBaggage.Any(
            static item => item.Key == "tenant.id" && item.Value == "north")),
};

var passed = 0;
for (var index = 0; index < checks.Length; index++)
{
    var check = checks[index];
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {index + 1}: {check.Message}");
    passed += check.Passed ? 1 : 0;
}

Console.WriteLine($"Verified {passed}/{checks.Length} checks on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}.");
Environment.ExitCode = passed == checks.Length ? 0 : 1;

static Dictionary<string, string> Inject(
    DistributedContextPropagator propagator,
    Activity activity)
{
    var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    propagator.Inject(activity, headers, static (carrier, key, value) =>
    {
        ((Dictionary<string, string>)carrier!)[key] = value;
    });

    return headers;
}

static (string? TraceParent, string? TraceState) ExtractParent(
    DistributedContextPropagator propagator,
    Dictionary<string, string> headers)
{
    propagator.ExtractTraceIdAndState(
        headers,
        GetHeader,
        out var traceParent,
        out var traceState);
    return (traceParent, traceState);
}

static void GetHeader(
    object? carrier,
    string key,
    out string? value,
    out IEnumerable<string>? values)
{
    value = ((Dictionary<string, string>)carrier!).GetValueOrDefault(key);
    values = null;
}

static bool HeadersEqual(
    Dictionary<string, string> left,
    Dictionary<string, string> right) =>
    left.Count == right.Count
    && left.All(pair => right.TryGetValue(pair.Key, out var value) && value == pair.Value);
