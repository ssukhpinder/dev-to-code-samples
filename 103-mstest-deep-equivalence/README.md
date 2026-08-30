# MSTest 4.3 deep structural equivalence

## Problem

`Assert.AreEqual` does not recursively compare ordinary DTO classes. Separate instances with the same public values fail by reference, while hand-written property assertions can miss a nested field. JSON snapshots add a serialization contract and a text artifact when the test only needs to compare two typed object graphs.

MSTest 4.3 introduced `Assert.AreEquivalent` for deep structural comparison. This sample verifies its behavior with separately allocated order DTOs, a nested address mismatch, reordered lines, extra dictionary keys, cycles, and shared-reference topology.

## Prerequisites

- .NET 10 SDK
- PowerShell 5.1 or later for `verify.ps1`
- network access for the first NuGet restore and vulnerability audit

The test project pins the stable `MSTest.Sdk` package at `4.3.3`. It needs no credentials, account, database, web server, paid API, clock, randomness, or runtime network request.

## Setup

From this folder:

```powershell
dotnet restore .\MSTestDeepEquivalence.slnx --nologo
dotnet format .\MSTestDeepEquivalence.slnx --verify-no-changes --no-restore
dotnet build .\MSTestDeepEquivalence.slnx --configuration Release --no-restore --nologo
```

`global.json` selects Microsoft Testing Platform for `dotnet test`.

## Run and test

Run the deterministic verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

Or run the test project directly:

```powershell
dotnet test --project .\tests\OrderContract.Tests\OrderContract.Tests.csproj --configuration Release --no-build --no-restore
```

## Expected behavior

All six tests pass, followed by:

```text
PASS: 6 MSTest deep-equivalence contract tests
```

The passing suite proves that separately allocated graphs can match, a nested value mismatch includes `ShippingAddress.City` in the diagnostic, sequence order remains significant, and `strict: true` rejects an extra dictionary key. It also verifies cycle handling and shared-reference topology without serialization.

## Limits

`Assert.AreEquivalent` is intentionally structural, not a replacement for domain-specific equality. Enumerables are order-sensitive. The default non-strict mode can ignore extra members or dictionary keys on the actual value, so contract tests should usually opt into `strict: true`. Comparison also stops recursing when the static type implements `IEquatable<T>`, and it inspects public instance fields and readable properties rather than private state.

Use semantic assertions when ordering is irrelevant, values need tolerances, or only a subset of fields belongs to the contract. See the official [MSTest assertion guide](https://learn.microsoft.com/en-us/dotnet/core/testing/unit-testing-mstest-writing-tests-assertions) and [`Assert.AreEquivalent` API rules](https://learn.microsoft.com/en-us/dotnet/api/microsoft.visualstudio.testtools.unittesting.assert.areequivalent?view=mstest-netfx-4.3) for the complete comparison behavior.
