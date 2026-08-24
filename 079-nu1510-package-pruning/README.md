# .NET 10 `NU1510` package pruning

## Problem

.NET 10 enables package pruning by default and raises `NU1510` when a direct `PackageReference` is already supplied by the target framework. Repositories that promote warnings to errors can therefore fail during restore after an upgrade. Removing every reference blindly is unsafe when the same project also targets an older framework that still needs the package.

This sample makes both decisions executable: remove a redundant net10-only `System.Text.Json` reference, but retain it only for `netstandard2.0` in a multi-target library.

## What the sample verifies

- `BrokenNet10` fails restore with `NU1510` because it directly references `System.Text.Json` 10.0.11 even though `net10.0` provides the assembly.
- `FixedNet10` removes the package reference, restores and builds cleanly, and still serializes JSON at runtime.
- `MultiTargetLibrary` conditions the reference on `netstandard2.0`, where the package is required.
- The multi-target package's `.nuspec` keeps `System.Text.Json` in the `netstandard2.0` dependency group and omits it from the `net10.0` group.
- The verifier inspects both `project.assets.json` files and the packed `.nuspec`; it does not infer success from console text alone.

## Prerequisites

- .NET SDK 10.0.303 or a later compatible .NET 10 SDK
- Access to the configured NuGet source during restore for `System.Text.Json` 10.0.11
- No credentials, database, container, paid service, or external runtime service

## Setup and verification

From this folder, run:

```powershell
dotnet restore .\Verifier\Verifier.csproj
dotnet build .\Verifier\Verifier.csproj --configuration Release --no-restore
dotnet run --project .\Verifier\Verifier.csproj --configuration Release --no-build --no-restore
dotnet format whitespace .\Nu1510PackagePruning.slnx --verify-no-changes --no-restore
```

The verifier owns the expected failure, so `BrokenNet10` is intentionally excluded from the solution. To reproduce only that restore failure, run:

```powershell
dotnet restore .\BrokenNet10\BrokenNet10.csproj --force --no-cache
```

To validate the supported projects independently:

```powershell
dotnet restore .\Nu1510PackagePruning.slnx
dotnet build .\Nu1510PackagePruning.slnx --configuration Release --no-restore
dotnet pack .\MultiTargetLibrary\MultiTargetLibrary.csproj --configuration Release --no-build --no-restore
```

## Expected behavior

The standalone `BrokenNet10` restore exits nonzero and reports `NU1510`. The verifier treats that failure as expected and ends with:

```text
PASS: the net10-only redundant reference fails restore under warnings-as-errors
PASS: the failure reports NU1510
PASS: the diagnostic names the redundant package
PASS: System.Text.Json still works after removing the package reference
PASS: the fixed net10 dependency graph has no System.Text.Json package
PASS: the package keeps System.Text.Json for netstandard2.0
PASS: the package omits System.Text.Json from its net10.0 dependency group
PASS: the conditioned reference is present for netstandard2.0
PASS: the conditioned reference is absent for net10.0
PASS 9/9
```

Generated packages, restored dependencies, and build output are ignored by the repository.

## Limitations

The sample uses `System.Text.Json` because the .NET SDK already knows when that package can be pruned. Custom packages are unaffected unless the SDK or a framework declares them with `PrunePackageReference`. Package pruning also depends on all runtime targets: if any target still needs the package, do not remove it globally. Condition the reference, restore every target, and inspect the packed dependency groups before shipping a library.

See Microsoft's [`NU1510` diagnostic reference](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1510), [.NET 10 breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1510-pruned-references), and [PackageReference pruning documentation](https://learn.microsoft.com/en-us/nuget/consume-packages/package-references-in-project-files#prunepackagereference).
