# .NET 10 configuration null binding verifier

## Problem

.NET 10 preserves JSON `null` values during configuration binding. That fixes the older empty-string behavior, but it also means an explicit `null` can overwrite a property initializer. For a non-nullable value type such as `int`, the binder assigns `default(T)`, so a retry count initialized to `3` can become `0` without a binding exception.

This console sample makes the upgrade boundary visible. It proves how missing values, explicit `null`, null array elements, and empty arrays bind, then uses a nullable draft object plus key-presence checks to reject unsafe configuration before constructing runtime options.

## Prerequisites

- .NET 10 SDK. The sample was verified with SDK `10.0.303` and runtime `10.0.11`.
- No credentials, external services, network calls, database, or paid API are required.

The project uses the shared `Microsoft.AspNetCore.App` framework for the configuration JSON provider and binder. It has no NuGet package dependencies.

## Setup and run

From this folder:

```powershell
dotnet restore
dotnet run --configuration Release
```

The fixtures are embedded in `Program.cs`, so every run uses the same input. The program exits nonzero on the first failed assertion.

## Expected behavior

The unsafe direct bind shows these .NET 10 results:

```text
Region=<null>
RetryCount=0
Endpoints=<null> | https://east.example
EmptyTargets.Count=0
```

The verifier then checks that missing scalar keys retain initializers, a missing array stays at its initial `null` state, explicit nulls are distinguishable from missing keys at the section boundary, unsafe input is rejected, and valid input produces a non-null runtime record. A successful run ends with:

```text
Verified 13/13 checks.
Accepted: region=ca-west; retries=4; endpoints=https://east.example,https://west.example
```

## Deterministic verification

Run the same checks used for the sample pull request:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
dotnet list package
dotnet list package --vulnerable --include-transitive
```

For an extra repeatability check, run the compiled program five times and compare the output. It contains no clock, random, locale, filesystem, or environment-dependent values.

## What the safer boundary changes

`DangerousWorkerOptions` binds directly into non-nullable runtime properties and demonstrates the risky `null`-to-`0` conversion. `WorkerOptionsDraft` instead makes input values nullable. `BindAndValidate` separately records which JSON keys were present, reports missing and explicit-null inputs differently, validates endpoint URIs, and only then creates the non-null `WorkerOptions` record.

This pattern is useful when `0` is a legitimate runtime value or when a property initializer is meant to be a default only for a missing key. It is deliberately small; production applications can put equivalent checks in options validation or their configuration startup gate.
