# .NET 10 System.Text.Json metadata conflicts

## Problem

Polymorphic `System.Text.Json` contracts reserve a property for type-discriminator metadata. If `TypeDiscriminatorPropertyName` uses the same name as a model property, .NET 9 can emit two properties with that name and then fail to deserialize its own output.

.NET 10 validates this contract earlier and throws `InvalidOperationException` before it emits ambiguous JSON. This sample runs the same broken contract on .NET 9 and .NET 10, then verifies a repair that keeps the domain `Type` property and moves the discriminator to `$kind`.

## Prerequisites

- .NET 10 SDK
- .NET 9 and .NET 10 runtimes
- PowerShell 5.1 or later for `verify.ps1`

The sample has no package dependencies, credentials, account, service, database, clock, randomness, or runtime network request. No credential placeholder is needed because there is no authenticated integration.

## Setup

From this folder:

```powershell
dotnet restore .\JsonMetadataConflicts.csproj --nologo
dotnet format .\JsonMetadataConflicts.csproj --verify-no-changes --no-restore
dotnet build .\JsonMetadataConflicts.csproj --configuration Release --no-restore --nologo
```

Restore and vulnerability-audit commands may contact configured NuGet sources even though the project has no package references.

## Run and test

Run the deterministic cross-runtime verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

You can also inspect each target directly:

```powershell
dotnet run --project .\JsonMetadataConflicts.csproj --framework net9.0 --configuration Release --no-build --no-restore
dotnet run --project .\JsonMetadataConflicts.csproj --framework net10.0 --configuration Release --no-build --no-restore
```

## Expected behavior

The verifier prints:

```text
PASS: .NET 9 reproduced ambiguous metadata output
PASS: .NET 10 rejected the conflicting contract before JSON emission
PASS: the $kind repair round-tripped on both runtimes
```

The .NET 9 run serializes two `Type` properties and then gets `JsonException` while reading the result. The .NET 10 run gets `InvalidOperationException` before JSON is produced. Both runtimes serialize and deserialize the repaired `$kind` contract successfully.

This check is intentionally about collisions between serializer metadata and model properties. It does not reject arbitrary duplicate properties in untrusted input, and ordinary non-polymorphic models are unaffected. Renaming a discriminator can also change a public wire contract, so coordinate that migration with consumers instead of silently changing production payloads.

See Microsoft's [.NET 10 compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/serialization/10/property-name-validation) and [polymorphism guide](https://learn.microsoft.com/en-us/dotnet/standard/serialization/system-text-json/polymorphism) for the full behavior and configuration options.
