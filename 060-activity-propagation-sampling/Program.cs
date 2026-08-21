using System.Diagnostics;

const string sourceName = "ActivityPropagationSampling";
var checks = 0;
var samplingDecision = ActivitySamplingResult.PropagationData;

using var source = new ActivitySource(sourceName, "1.0.0");
using var listener = new ActivityListener
{
    ShouldListenTo = candidate => candidate.Name == sourceName,
    Sample = (ref ActivityCreationOptions<ActivityContext> _) => samplingDecision
};

ActivitySource.AddActivityListener(listener);

var recordedParent = new ActivityContext(
    ActivityTraceId.CreateFromString("11111111111111111111111111111111"),
    ActivitySpanId.CreateFromString("2222222222222222"),
    ActivityTraceFlags.Recorded,
    traceState: null,
    isRemote: true);

using var propagationOnly = source.StartActivity(
    "receive-message",
    ActivityKind.Consumer,
    recordedParent);

Check(propagationOnly is not null, "PropagationData creates an activity");
Check(!propagationOnly!.Recorded, ".NET 10 does not inherit Recorded");
Check(!propagationOnly.IsAllDataRequested, "PropagationData skips enrichment data");
Check(propagationOnly.TraceId == recordedParent.TraceId, "trace ID is preserved");
Check(propagationOnly.ParentSpanId == recordedParent.SpanId, "parent span is preserved");

propagationOnly.ActivityTraceFlags |= ActivityTraceFlags.Recorded;

Check(propagationOnly.Recorded, "explicit compatibility override records the activity");
Check(
    (propagationOnly.ActivityTraceFlags & ActivityTraceFlags.Recorded) != 0,
    "recorded flag will propagate downstream");

samplingDecision = ActivitySamplingResult.AllDataAndRecorded;

using var fullyRecorded = source.StartActivity(
    "record-message",
    ActivityKind.Consumer,
    recordedParent);

Check(fullyRecorded is not null, "AllDataAndRecorded creates an activity");
Check(fullyRecorded!.Recorded, "AllDataAndRecorded records the activity");
Check(fullyRecorded.IsAllDataRequested, "AllDataAndRecorded requests enrichment data");

Console.WriteLine($"PASS: {checks}/10 checks");

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}
