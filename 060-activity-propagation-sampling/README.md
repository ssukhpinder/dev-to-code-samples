# .NET 10 Activity `PropagationData` sampling contract

## Problem

In .NET 10, a custom `ActivityListener.Sample` callback that returns
`ActivitySamplingResult.PropagationData` no longer makes a child activity
`Recorded` merely because its parent is recorded. The child still carries the
trace identity, but `Recorded` and `IsAllDataRequested` are both `false`.

That is the intended, OpenTelemetry-aligned contract. Code that directly owns a
custom listener should test the decision it actually returns instead of relying
on the parent's flag.

This sample creates a fixed recorded remote parent and proves three paths:

1. `PropagationData` preserves the trace and parent span IDs without recording
   the child.
2. The documented compatibility override can set the child's `Recorded` flag
   explicitly.
3. `AllDataAndRecorded` records the child and requests tags, links, and events.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- No packages, collector, exporter, network connection, or credentials.

## Setup and validation

From this folder, run:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --vulnerable --include-transitive
```

The executable is the deterministic verifier. It exits nonzero on the first
contract failure.

## Expected behavior

The run ends with:

```text
PASS: PropagationData creates an activity
PASS: .NET 10 does not inherit Recorded
PASS: PropagationData skips enrichment data
PASS: trace ID is preserved
PASS: parent span is preserved
PASS: explicit compatibility override records the activity
PASS: recorded flag will propagate downstream
PASS: AllDataAndRecorded creates an activity
PASS: AllDataAndRecorded records the activity
PASS: AllDataAndRecorded requests enrichment data
PASS: 10/10 checks
```

The parent trace ID and span ID are fixed test data. The verifier prints no
generated activity IDs, timestamps, random values, environment-dependent paths,
or locale-dependent values, so repeated runs produce identical output.

## Choosing the sampling result

Use `PropagationData` when an activity only needs enough state to carry trace
identity and baggage downstream. Use `AllData` when local enrichment is needed
without setting the recorded flag. Use `AllDataAndRecorded` when the listener
intentionally requests both enrichment and recording.

Setting `ActivityTraceFlags.Recorded` after creation is a narrow compatibility
measure documented for code that relied on the pre-.NET 10 behavior. It does not
change `IsAllDataRequested`, so it is not a substitute for choosing the correct
sampling result.

## Limitations

- The behavior matters to code that directly implements a custom
  `ActivityListener.Sample` callback and returns `PropagationData`.
- The default OpenTelemetry .NET parent-based sampler is not affected by this
  change.
- This verifier does not run an exporter or collector; it tests the in-process
  sampling contract only.

See Microsoft's [.NET 10 compatibility note](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/10.0/activity-sampling)
and the [`ActivitySamplingResult` API](https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysamplingresult?view=net-10.0)
for the platform contract.
