# .NET 10 W3C trace-context verifier

## Problem

.NET 10 changes the process-wide default `DistributedContextPropagator` from the legacy propagator to the W3C propagator. Custom transports or downstream consumers that only inspect `Correlation-Context` can silently lose baggage because the new default emits the standard `baggage` header instead.

This sample compares the .NET 10 default, explicit W3C, and pre-W3C propagators against dictionary-backed carriers. It also verifies how they handle a W3C `traceparent`, a hierarchical `Request-Id`, and legacy inbound baggage.

## Prerequisites

- .NET 10 SDK (validated with SDK 10.0.303 and runtime 10.0.11)
- No credentials, collector, open port, or external service

## Setup and run

From this folder:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
```

The verifier uses fixed input IDs and prints only pass/fail results. A successful run ends with:

```text
PASS 1: The process default matches the explicit W3C propagator
PASS 2: The W3C traceparent keeps the upstream trace ID
PASS 3: The .NET 10 default emits baggage, not Correlation-Context
PASS 4: The pre-W3C propagator emits Correlation-Context, not baggage
PASS 5: The default extracts a valid W3C parent and tracestate
PASS 6: The default rejects a hierarchical Request-Id parent
PASS 7: The pre-W3C propagator accepts a hierarchical Request-Id parent
PASS 8: The default still reads legacy inbound baggage during migration
Verified 8/8 checks on .NET 10.0.11.
```

## Deterministic verification

Run the verifier repeatedly and compare its output:

```powershell
1..5 | ForEach-Object { dotnet run -c Release --no-build }
```

Check the dependency graph and current advisories:

```bash
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The project has no package references. Restore and advisory checks may contact configured NuGet sources; the verifier itself performs no network calls.

## Limitations

- This checks `DistributedContextPropagator` directly. It does not stand up HTTP services, a message broker, or an OpenTelemetry collector.
- The pre-W3C fallback is process-wide when assigned to `DistributedContextPropagator.Current`. Use it only as a temporary compatibility measure while consumers migrate.
- Baggage travels across process boundaries as headers. Do not place secrets or sensitive personal data in it.

The behavior is documented in Microsoft's [.NET 10 compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/default-trace-context-propagator) and the [`DistributedContextPropagator` API reference](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.distributedcontextpropagator?view=net-10.0).
