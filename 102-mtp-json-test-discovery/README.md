# Microsoft.Testing.Platform 2.3 JSON test discovery

## Problem

A green test command does not prove that every intended test was discovered. A renamed method, missing attribute, source-generator change, or project-selection mistake can silently shrink the suite before execution begins.

Microsoft.Testing.Platform 2.3 adds `--list-tests json` to the test application. This sample turns that machine-readable discovery output into a small contract: three expected MSTest methods and their traits must match `expected-tests.json` exactly.

## Prerequisites

- .NET 10 SDK
- PowerShell 5.1 or later for `verify.ps1`
- network access for the first NuGet restore and vulnerability audit

The project pins the stable `MSTest.Sdk` package at `4.3.3`, which uses Microsoft.Testing.Platform `2.3.3`. It needs no credentials, account, database, web server, paid API, clock, randomness, or runtime network request.

## Setup

From this folder:

```powershell
dotnet restore .\JsonTestDiscovery.slnx --nologo
dotnet format .\JsonTestDiscovery.slnx --verify-no-changes --no-restore
dotnet build .\JsonTestDiscovery.slnx --configuration Release --no-restore --nologo
```

`global.json` selects Microsoft Testing Platform for `dotnet test`. The MSTest project is also an executable, so its platform options can be passed directly after `--`.

## Run and test

Inspect the raw discovery document without executing the tests:

```powershell
dotnet run --project .\tests\Inventory.Tests\Inventory.Tests.csproj --configuration Release --no-build --no-restore -- --list-tests json --no-banner
```

Run the inventory guard:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

Run the discovered tests normally:

```powershell
dotnet test --project .\tests\Inventory.Tests\Inventory.Tests.csproj --configuration Release --no-build --no-banner
```

## Expected behavior

The verifier prints:

```text
PASS: parsed Microsoft.Testing.Platform discovery schema version 1
PASS: exact inventory matched 3 test methods and traits
```

The guard compares namespace, type, method, and sorted traits. It deliberately ignores generated UIDs, display names, source locations, and absolute paths because those are poor baseline keys. Removing a test attribute, renaming a method, changing a trait, or adding an unapproved test makes the verifier fail with the inventory difference.

This is an exact inventory check, not a replacement for running tests. Parameterized cases may expand into several discovered entries, and framework upgrades can intentionally change discovery metadata. Review and update the manifest when that change is expected. Discovery also loads the test application and framework, so discovery hooks should remain free of external side effects.
