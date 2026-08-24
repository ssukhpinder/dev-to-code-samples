# .NET 10 RID-specific tool packaging

## Problem

Starting with .NET SDK 10, a tool project that sets `RuntimeIdentifiers` no longer gets one framework-dependent, platform-agnostic package from `dotnet pack`. The SDK creates a top-level pointer package plus a platform-specific package for each runtime identifier. That is useful when the split is intentional, but it can surprise a CI job that expects one `.nupkg` or publishes only the pointer.

This sample turns the generated package set into an explicit contract. It verifies the new default, narrows the set with `ToolPackageRuntimeIdentifiers`, and demonstrates the documented opt-out for a single portable tool package.

## What the sample verifies

- `Tool` is a minimal `PackAsTool` project targeting `net10.0` with `win-x64` and `linux-x64` in `RuntimeIdentifiers`.
- The default pack creates three artifacts: one pointer package and one package for each RID.
- The pointer package maps each RID to the correct package ID in version 2 `DotnetToolSettings.xml`.
- Setting `ToolPackageRuntimeIdentifiers=win-x64` produces only the pointer and `win-x64` packages.
- Removing `RuntimeIdentifiers` for the portable pack and setting both `CreateRidSpecificToolPackages=false` and `UseAppHost=false` restores one framework-dependent package under the `any` tool path.
- `Verifier` runs all three pack modes and inspects package names, tool settings, and ZIP entry paths instead of trusting console text.

## Prerequisites

- .NET SDK 10.0.303 or a later compatible .NET 10 SDK
- No credentials, authenticated/private package feed, paid service, or runtime network call
- Restore and the optional vulnerability audit can contact configured public NuGet sources to obtain RID apphost/runtime packs and advisory data

## Setup and verification

From this folder, run:

```powershell
dotnet restore ./RidToolPackaging.slnx
dotnet format whitespace ./RidToolPackaging.slnx --verify-no-changes --no-restore
dotnet build ./RidToolPackaging.slnx --configuration Release --no-restore
dotnet run --project ./Verifier/PackageVerifier.csproj --configuration Release --no-build --no-restore
dotnet package list --project ./RidToolPackaging.slnx --vulnerable --include-transitive --no-restore
```

The verifier writes generated packages only below `artifacts/verifier`, which is covered by the repository's root `.gitignore`.

## Expected behavior

The verifier reports the default package split, the narrowed RID set, and the portable opt-out. Representative lines are:

```text
PASS: RuntimeIdentifiers creates a pointer package and one package per RID
PASS: ToolPackageRuntimeIdentifiers limits the package set
PASS: the documented opt-out restores one portable package
PASS 16/16
```

The exact `.nupkg` sizes and hashes are deliberately not asserted because servicing updates can change host and runtime files without changing the packaging contract.

## Limitations

This sample's RID outputs are platform-specific but framework-dependent, so the target machine still needs .NET 10. It does not set `SelfContained`, enable trimming, or use Native AOT. AOT targets must be packed separately and the build OS must match the target OS. The sample also does not publish or install the packages. For a real feed, publish every RID package before the pointer package so installation never resolves an unavailable dependency.

Use `ToolPackageRuntimeIdentifiers` when only tool packaging needs a narrower RID list. Conditionally omit `RuntimeIdentifiers` and use the documented two-property opt-out when the tool should remain framework-dependent and platform-agnostic. Adding `any` is another supported option when you want both RID-specific packages and a portable fallback.

See Microsoft's [.NET 10 packaging compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/dotnet-tool-pack-publish), [RID-specific tool guide](https://learn.microsoft.com/en-us/dotnet/core/tools/rid-specific-tools), and [.NET 10.0.11 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.11.md).
