# xUnit 4 `Assert.All` empty-collection guard

## Problem

`Assert.All(collection, assertion)` verifies every item that exists, but an empty collection has no failing item. A repository or API contract test can therefore pass after a query unexpectedly returns zero rows.

xUnit.net v3 4.0 adds the `throwIfEmpty` overload. This sample makes the difference deterministic with three build configurations:

- `LegacyControl` supplies an empty result to the two-argument overload, which passes without executing the item assertion.
- `StrictFailure` supplies the same empty result to the new overload, which fails with an explicit empty-collection message.
- `Release` supplies two valid rows to the strict overload, which passes after checking both items.

## Prerequisites

- .NET 10 SDK
- PowerShell 5.1 or later for `verify.ps1`
- network access for the first NuGet restore and vulnerability audit

The project pins the stable `xunit.v3.mtp-v2` package at `4.0.0`. It needs no credentials, account, database, web server, paid API, clock, randomness, or runtime network request.

## Setup

From this folder:

```powershell
dotnet restore .\StrictAll.slnx --nologo
dotnet format .\StrictAll.slnx --verify-no-changes --no-restore
dotnet build .\StrictAll.slnx --configuration Release --no-restore --nologo
```

`global.json` selects Microsoft Testing Platform for `dotnet test`, and the test project is also a stand-alone executable that can run through `dotnet run`.

## Run and test

Run the deterministic verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

It checks all three contracts. You can also inspect the configurations directly:

```powershell
dotnet run --project .\tests\StrictAll.Tests\StrictAll.Tests.csproj --configuration LegacyControl --no-restore
dotnet run --project .\tests\StrictAll.Tests\StrictAll.Tests.csproj --configuration StrictFailure --no-restore
dotnet run --project .\tests\StrictAll.Tests\StrictAll.Tests.csproj --configuration Release --no-restore
```

The middle command intentionally returns Microsoft Testing Platform exit code `2`. It represents the regression guard doing its job, not a broken sample.

## Expected behavior

The verifier prints:

```text
PASS: the two-argument overload reproduced the empty-result false pass
PASS: throwIfEmpty rejected the same empty result
PASS: throwIfEmpty checked a nonempty valid result
```

Use `throwIfEmpty: true` when an empty collection makes the test meaningless: authorization assignments, seeded lookup rows, search fixtures, or reconciliation results that must contain at least one item. Keep the two-argument overload when an empty result is valid and the only contract is that every returned item satisfies an invariant.

This overload proves only non-emptiness plus the supplied per-item assertion. It does not prove an exact count, ordering, uniqueness, or the presence of a specific record; add separate assertions for those contracts.
