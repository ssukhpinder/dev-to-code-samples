# .NET 10 `DefineConstants` MSBuild conditions

## Problem

.NET SDK 10 computes target-framework constants such as `NET9_0_OR_GREATER` during target execution instead of project evaluation. An `ItemGroup` or `PropertyGroup` condition that inspects `DefineConstants` can therefore stop matching and silently drop a package, analyzer, source file, or project reference.

This sample keeps the broken gate as an expected failure and replaces it with the documented MSBuild target-framework compatibility function.

## What the sample verifies

- `BrokenConsumer` asks `DefineConstants` for `NET9_0_OR_GREATER` during evaluation, loses its local `ProjectReference`, and still builds because nothing consumes the missing item.
- `FixedConsumer` uses `IsTargetFrameworkCompatible('net10.0', 'net9.0')`, keeps the reference, and builds.
- `Verifier` reads the evaluated `ProjectReference` items with `dotnet msbuild -getItem`, proves the broken project can pass an ordinary build, invokes an explicit `DCG001` guard, and runs the repaired consumer.
- The repaired program also proves that the C# `#if NET9_0_OR_GREATER` compile symbol is still present. The change is about MSBuild evaluation, not C# conditional compilation.

## Prerequisites

- .NET SDK 10.0.303 or a later compatible .NET 10 SDK
- No credentials, external services, package dependencies, or runtime network calls

## Setup and verification

From this folder, run:

```powershell
dotnet restore .\DefineConstantsConditions.slnx
dotnet format whitespace .\DefineConstantsConditions.slnx --verify-no-changes --no-restore
dotnet build .\Verifier\Verifier.csproj --configuration Release --no-restore
dotnet run --project .\Verifier\Verifier.csproj --configuration Release --no-build --no-restore
```

The verifier performs both the expected-failure and repaired builds. To inspect them separately:

```powershell
dotnet build .\BrokenConsumer\BrokenConsumer.csproj --configuration Release --no-restore
dotnet msbuild .\BrokenConsumer\BrokenConsumer.csproj -target:VerifyFrameworkReference -property:Configuration=Release
dotnet build .\FixedConsumer\FixedConsumer.csproj --configuration Release --no-restore
```

The ordinary broken build succeeds, showing why the vanished item is easy to miss. Its explicit verification target must fail with `DCG001`; the fixed build must succeed.

## Expected behavior

The verifier ends with:

```text
PASS: the broken project still builds, making the missing item easy to miss
PASS: the explicit gate fails with the evaluation diagnostic
PASS: the fixed project builds
PASS: C# compile symbols remain available after the MSBuild evaluation change
PASS 7/7
```

## Limitations

This sample demonstrates a project-evaluation condition with one local `ProjectReference`; it does not claim every use of `DefineConstants` is wrong. User-controlled constants still work, and C# `#if` symbols remain available during compilation. For an exact TFM match, comparing `$(TargetFramework)` can be clearer. Use `IsTargetFrameworkCompatible` when the intent is “this TFM or a compatible newer target.”

See Microsoft's [.NET 10 breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/defineconstants-not-available-at-evaluation), [MSBuild target-framework functions](https://learn.microsoft.com/en-us/visualstudio/msbuild/property-functions#msbuild-targetframework-and-targetplatform-functions), and [build evaluation overview](https://learn.microsoft.com/en-us/visualstudio/msbuild/build-process-overview).
