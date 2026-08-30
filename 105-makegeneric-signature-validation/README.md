# .NET 10 MakeGenericSignatureType validation

## Problem

`Type.MakeGenericSignatureType` accepted a non-generic type as its first argument before .NET 10, even though the resulting signature type had no meaningful behavior. Starting with .NET 10, the API requires a generic type definition and throws `ArgumentException` for a non-generic or closed generic input.

This sample multi-targets .NET 9 and .NET 10. It reproduces the changed boundary, then verifies a small guard that returns non-definitions unchanged and calls `MakeGenericSignatureType` only for an open generic definition.

## Prerequisites

- .NET 10 SDK
- .NET 9 and .NET 10 runtimes for running both target frameworks

The project has no package dependencies, credentials, external services, database, clock, or randomness.

## Setup

From this folder, restore both target frameworks:

```powershell
dotnet restore
```

## Build and verify

Check formatting and build both targets:

```powershell
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
```

Run the deterministic verifier on each runtime:

```powershell
dotnet run -c Release -f net9.0 --no-build
dotnet run -c Release -f net10.0 --no-build
```

Each run exits with code `0` and ends with:

```text
Summary: 5/5 passed
```

The .NET 9 run proves that the unguarded API accepts `string` as the supposed generic type definition. The .NET 10 run proves that the same call now throws `ArgumentException`. Both runs verify that the guarded helper preserves non-generic and already-constructed generic types while creating a signature with the expected definition and arguments for `Dictionary<,>`.

## Limitations

`MakeGenericSignatureType` is a specialized API for representing types in reflection signatures; it is not the usual way to create an invokable runtime type. Use `MakeGenericType` when the goal is to construct a normal closed generic type.

The pass-through fallback matches Microsoft's migration guidance and is useful when a helper accepts both generic definitions and ordinary types. If a non-definition indicates a caller bug in your library, validate `IsGenericTypeDefinition` and throw a domain-specific error instead of silently returning the input.
