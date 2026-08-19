# xUnit 4 `ParallelMode.All` safety

## Problem

xUnit.net v3 4.0 adds opt-in full test-case parallelization. With `ParallelMode.All`, tests in the same class, theory rows, and tests that share collection context may overlap. A suite that was safe under the default `ParallelMode.Collections` can expose races in static state, files, databases, or shared fixtures after opting in.

This sample makes the risk deterministic, then shows two guardrails:

- use thread-safe operations for state that is intentionally shared by parallel tests;
- opt a test out with `DisableParallelization` when its resource must remain exclusive.

The unsafe project is expected to fail. Its two theory rows read the same counter value before either writes, so both write `1` and both assertions observe the lost update. The safe project uses `Interlocked` for the parallel path and serializes two shared-state cases with a one-at-a-time lease. The verifier also compiles a control configuration that removes the opt-out; two barriers then force both control rows to contend for that lease, and exactly one fails.

## Prerequisites

- .NET 10 SDK
- network access for the first NuGet restore
- PowerShell 5.1 or later for `verify.ps1`

No credentials, external services, paid APIs, or mutable machine-wide test resources are required.

## Setup

From this folder:

```powershell
dotnet restore .\ParallelSafety.slnx --nologo
dotnet build .\ParallelSafety.slnx --configuration Release --no-restore --nologo
```

Both projects pin the stable `xunit.v3.mtp-v2` package at `4.0.0`. `global.json` selects Microsoft Testing Platform for `dotnet test`.

## Run and verify

Run the deterministic verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

It performs four checks:

1. `UnsafeRaceDemo` returns Microsoft Testing Platform exit code `2`, with exactly two failed theory rows.
2. The two same-class `SafeParallelTests` rows overlap and pass with atomic state.
3. A control build without `DisableParallelization` returns exit code `2`, with exactly one lease-conflict failure.
4. The same two shared-state rows pass when the opt-out is present.

You can run the projects directly when inspecting their output:

```powershell
dotnet test --project .\tests\UnsafeRaceDemo\UnsafeRaceDemo.csproj --configuration Release --no-build --no-restore
dotnet test --project .\tests\SafeParallelTests\SafeParallelTests.csproj --configuration Release --no-build --no-restore
```

## Expected behavior

The verifier prints:

```text
PASS: ParallelMode.All reproduced the two-row lost update
PASS: two same-class cases overlapped with atomic state
PASS: removing DisableParallelization reproduced the lease conflict
PASS: DisableParallelization serialized the shared-state cases
```

The sample is a scheduling and isolation regression, not a benchmark. The default xUnit 4 mode remains `ParallelMode.Collections`; `All` must be selected explicitly. A runner may override assembly settings, and `MaxThreads` should be sized for the actual CI environment. Use collection-, class-, method-, data-source-, or data-row-level opt-outs when the protected resource has a wider or narrower lifetime than one fact.
