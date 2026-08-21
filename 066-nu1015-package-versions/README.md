# .NET 10 `NU1015` package versions

## Problem

.NET 10 turns a direct `PackageReference` without a version into restore error `NU1015`. That is the right failure for an unversioned direct dependency, but the same XML is valid when NuGet Central Package Management (CPM) supplies the version from `Directory.Packages.props`.

This sample reproduces both cases with a generated local package feed. It distinguishes an accidental missing version from an intentional centrally managed reference, then proves the resolved version in each repair.

## What the sample verifies

- `BrokenDirect` fails its default .NET 10 restore with `NU1015` for `Demo.Greeting`.
- Lowering `SdkAnalysisLevel` to `9.0.300` restores the previous `NU1604` warning. The verifier also disables warning promotion for that one restore, then proves the fallback selects the lowest available version, `1.0.0`. This is a compatibility demonstration, not the repair.
- `FixedDirect` adds `Version="2.0.0"` to its direct `PackageReference` and resolves `2.0.0`.
- `CentralManaged` intentionally omits `Version` while `Directory.Packages.props` enables CPM and pins `2.0.0`.
- `Verifier` creates fixture packages `1.0.0` and `2.0.0`, runs every restore, inspects each `project.assets.json`, builds the repaired projects, and checks their runtime output.

## Prerequisites

- .NET SDK 10.0.303 or a later compatible .NET 10 SDK
- PowerShell only for the command examples; the verifier itself is cross-platform .NET
- No credentials, external services, pre-existing packages, or runtime network access

## Setup and verification

From this folder, run:

```powershell
dotnet restore .\Verifier\Verifier.csproj
dotnet build .\Verifier\Verifier.csproj --configuration Release --no-restore
dotnet run --project .\Verifier\Verifier.csproj --configuration Release --no-build --no-restore
dotnet format whitespace .\Nu1015PackageVersions.slnx --verify-no-changes --no-restore
```

The verifier builds the local feed before restoring the three consumer projects. The solution intentionally excludes `BrokenDirect` so ordinary formatting and builds stay clean; the verifier owns that expected-failure fixture. To reproduce only the .NET 10 error after the verifier has created the feed, run:

```powershell
dotnet restore .\BrokenDirect\BrokenDirect.csproj --force --no-cache
```

To inspect the two supported fixes independently:

```powershell
dotnet restore .\FixedDirect\FixedDirect.csproj --force --no-cache
dotnet restore .\CentralManaged\CentralManaged.csproj --force --no-cache
```

## Expected behavior

The first standalone `BrokenDirect` restore exits nonzero and reports `NU1015`. The verifier treats that failure as expected and ends with:

```text
PASS: the default broken restore reports NU1015 for Demo.Greeting
PASS: the compatibility level restores the old NU1604 warning
PASS: the direct fix resolves package version 2.0.0
PASS: Central Package Management resolves package version 2.0.0
PASS 12/12
```

Generated packages, restored dependencies, and build output are ignored by the repository.

## Limitations

The local feed keeps the result deterministic; it does not model authentication, source mapping, lock files, or private-feed availability. `SdkAnalysisLevel=9.0.300` changes every SDK feature gated by that level, and a build that promotes warnings to errors still rejects `NU1604` unless that policy is relaxed. The fallback can also silently restore the lowest package version, so I would use it only as a short diagnostic bridge. For a direct reference, add an explicit version. For CPM, verify that the nearest `Directory.Packages.props` enables `ManagePackageVersionsCentrally` and contains the matching `PackageVersion`.

See Microsoft's [.NET 10 breaking-change guidance](https://learn.microsoft.com/en-us/dotnet/core/compatibility/sdk/10.0/nu1015-packagereference-version), the [NU1015 diagnostic reference](https://learn.microsoft.com/en-us/nuget/reference/errors-and-warnings/nu1015), and the [Central Package Management guide](https://learn.microsoft.com/en-us/nuget/consume-packages/central-package-management).
