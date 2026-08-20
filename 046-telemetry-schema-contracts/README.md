# .NET 10 telemetry schema URL contract guard

## Problem

Traces and metrics can keep exporting after their names, attributes, or versions drift apart. A backend then receives two signals that claim different telemetry contracts, and the mismatch is easy to miss when tests only assert individual span and counter values.

This dependency-free .NET 10 console sample assigns the same versioned HTTPS `TelemetrySchemaUrl` to `ActivitySourceOptions` and `MeterOptions`. An offline guard compares the source and meter metadata with the application's expected contract before any exporter is involved. It deliberately pairs the current trace source with a stale metric schema URL to prove that both stale metadata and cross-signal drift are detected.

The program also uses `ActivityListener` and `MeterListener` to capture one fixed activity and one fixed counter measurement. Its assertions cover the operation, status, value, unit, tags, instrumentation version, and schema URL without sending telemetry anywhere.

## Prerequisites

- A stable .NET 10 SDK
- The local verification run used the .NET 10.0.303 SDK and `Microsoft.NETCore.App` runtime 10.0.11
- No NuGet package, credential, account, collector, exporter, external service, environment variable, or network call is required

The schema option APIs are documented on [`ActivitySourceOptions.TelemetrySchemaUrl`](https://learn.microsoft.com/dotnet/api/system.diagnostics.activitysourceoptions.telemetryschemaurl?view=net-10.0) and [`MeterOptions.TelemetrySchemaUrl`](https://learn.microsoft.com/dotnet/api/system.diagnostics.metrics.meteroptions.telemetryschemaurl?view=net-10.0).

## Setup and deterministic verification

From this folder, run:

```powershell
dotnet restore TelemetrySchemaContracts.csproj
dotnet format TelemetrySchemaContracts.csproj --verify-no-changes --no-restore
dotnet build TelemetrySchemaContracts.csproj -c Release --no-restore
dotnet run --project TelemetrySchemaContracts.csproj -c Release --no-build
dotnet run --project TelemetrySchemaContracts.csproj -c Release --no-build
dotnet list TelemetrySchemaContracts.csproj package --include-transitive
dotnet list TelemetrySchemaContracts.csproj package --vulnerable --include-transitive
```

The two verifier runs should print the same result:

```text
PASS matching trace and metric metadata satisfies the contract
PASS both signals expose the expected versioned HTTPS schema URL
PASS the offline guard rejects a stale metric schema URL
PASS the offline guard reports cross-signal schema drift
PASS ActivityListener captures exactly one completed activity
PASS the activity has the deterministic operation, status, and tags
PASS the trace listener observes the source schema metadata
PASS MeterListener captures exactly one counter measurement
PASS the metric has the deterministic value, unit, and tags
PASS the metric listener observes the meter schema metadata
10/10 checks passed.
```

The process exits nonzero if any contract, activity, or measurement assertion fails. All values are constants; the program does not use current time, randomness, files, sockets, DNS, HTTP, or an exporter. The `schemas.example.com` URL is intentionally an illustrative reserved-domain URL, and the guard only examines its metadata in memory.

## What the guard checks

`TelemetryContractGuard` compares the instrumentation name and version on both producers. It then requires both schema values to be absolute HTTPS URLs, requires the expected version segment in each path, compares each value with the configured contract, and confirms that traces and metrics agree with each other.

That turns a stale schema URL into a deterministic build or test failure. The listener snapshots separately prove that the configured schema metadata is reachable from the `ActivitySource` behind a completed activity and from the `Meter` behind a counter measurement.

## Limitations

`TelemetrySchemaUrl` is metadata. Setting it does not fetch, parse, validate, cache, or transform a schema, and this sample's guard does not prove that a document exists or that emitted names and attributes conform to its contents. Use a real, stable, retrievable HTTPS URL in production, version the schema deliberately, and add a schema-aware compatibility check when the document format supports one.

The in-process listeners are a focused contract test, not an exporter or collector integration test. A production pipeline still needs separate coverage for SDK/exporter mapping, batching, transport, collector processing, backend ingestion, and any schema translation it claims to perform.
