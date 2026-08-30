# .NET 10 generic math shift masking

## Problem

.NET 10 changed generic shifts on `byte`, `char`, `sbyte`, `short`, and `ushort` so their shift counts are masked consistently. A generic helper that accidentally shifts a `byte` by `8` can therefore return `1` on .NET 10 where the same helper returned `0` on .NET 9.

This sample runs identical `IShiftOperators<T, int, T>` code on both runtimes. It also shows two explicit application policies: reject counts outside the value width, or normalize them with modulo arithmetic.

## Prerequisites

- .NET 10 SDK
- .NET 9 and .NET 10 runtimes
- PowerShell 5.1 or later for `verify.ps1`

The sample has no package dependencies, credentials, account, service, database, clock, randomness, or runtime network request. No credential placeholder is needed because there is no authenticated integration.

## Setup

From this folder:

```powershell
dotnet restore .\GenericMathShiftMasking.csproj --nologo
dotnet format .\GenericMathShiftMasking.csproj --verify-no-changes --no-restore
dotnet build .\GenericMathShiftMasking.csproj --configuration Release --no-restore --nologo
```

Restore and vulnerability-audit commands may contact configured NuGet sources even though the project has no package references.

## Run and test

Run the deterministic cross-runtime verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

You can also inspect each target directly:

```powershell
dotnet run --project .\GenericMathShiftMasking.csproj --framework net9.0 --configuration Release --no-build --no-restore
dotnet run --project .\GenericMathShiftMasking.csproj --framework net10.0 --configuration Release --no-build --no-restore
```

## Expected behavior

The verifier prints:

```text
PASS: .NET 9 reproduced the previous small-integer overshift behavior
PASS: .NET 10 masked small-integer shift counts consistently
PASS: explicit reject and modulo policies stayed runtime-independent
```

The direct runs show the behavioral boundary. For example, `byte-left-8` is `0` on .NET 9 and `1` on .NET 10, while the `int-left-32-control` result is `1` on both. The explicit modulo policy returns `2` for `byte << 9` on both runtimes, and the reject policy throws `ArgumentOutOfRangeException` on both.

Use rejection when an oversized count means malformed input, such as a protocol field or serialized bit offset. Use explicit normalization only when cyclic shift counts are part of the contract.

This sample covers built-in integer implementations through generic math. Custom types still define their own shift semantics, and this is not a replacement for reviewing rotate, sign-extension, or cryptographic bit-manipulation code separately.
