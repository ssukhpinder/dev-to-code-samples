# .NET 10 InlineArray explicit-size migration

## Problem

.NET 10 rejects a value type that combines `InlineArrayAttribute` with an explicit `StructLayoutAttribute.Size`. Older runtimes accepted that ambiguous metadata, so an upgraded application can encounter `TypeLoadException` only when the type is first loaded.

The supported migration depends on what the explicit byte count meant: put `Size` on a wrapper around the whole inline array, or put it on the element type and let `InlineArray` repeat that element.

## What this sample demonstrates

The verifier creates the legacy metadata with `Reflection.Emit` and proves that .NET 10 rejects it. It then checks two supported shapes:

- `WholeArrayWrapper` applies `Size = 32` to a struct that contains an eight-element `int` inline array.
- `SizedElementArray` repeats a four-byte element type eight times without placing `Size` on the inline-array type itself.

Both fixed shapes are verified for byte size and indexed value preservation.

## Prerequisites

- .NET 10 SDK

No package dependency, credential, native library, external service, or runtime network request is required. Restore and vulnerability-audit commands may still inspect configured NuGet sources.

## Setup and run

From this folder:

```bash
dotnet restore
dotnet run --configuration Release --no-restore
```

Expected behavior:

```text
PASS legacy InlineArray plus explicit Size is rejected
PASS plain eight-int InlineArray occupies 32 bytes
PASS whole-array wrapper occupies 32 bytes
PASS sized-element InlineArray occupies 32 bytes
PASS whole-array wrapper preserves all values
PASS sized-element InlineArray preserves all values
Legacy load result: TypeLoadException
Verifier: 6/6 passed
```

The process exits with code `0` only when all six checks pass.

## Deterministic verification

Run the complete validation used for this sample:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --project InlineArrayExplicitSize.csproj --configuration Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The fixture is created in memory, the values and expected sizes are fixed, and no check depends on a clock, locale, randomness, filesystem state, or external process.

## Limitations

The emitted fixture isolates the .NET 10 loader rule; it is not a substitute for loading every legacy assembly in an upgrade test. A real application might force the affected type during startup, reflection, interop registration, or ahead-of-time compilation. The byte-size checks also do not prove that a layout matches a particular native ABI. Verify field order, packing, alignment, and the native declaration for each interop boundary.
