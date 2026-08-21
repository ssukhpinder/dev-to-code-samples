# .NET 10 `System.Linq.AsyncEnumerable` ambiguity

## Problem

.NET 10 includes LINQ operators for `IAsyncEnumerable<T>` in the platform. A project that still compiles against `System.Linq.Async` 6.0.1 can expose two applicable `Select` extension methods and fail with `CS0121` after the target-framework upgrade.

This sample keeps the failure as an expected compile probe and verifies two repairs documented by Microsoft:

- remove `System.Linq.Async` when the .NET 10 platform operators cover the application;
- when an indirect dependency still needs the old package at runtime, exclude its compile assets so application code binds to the platform operators.

## What the sample verifies

- `CollisionProbe` deliberately references `System.Linq.Async` 6.0.1 and must fail with `CS0121`.
- `PlatformFix` removes the package, uses the built-in `Select` and `Take` operators, and replaces the old `SelectAwait` call shape with the .NET 10 cancellation-aware `Select` overload.
- `TransitiveFix` retains the package with `<ExcludeAssets>compile</ExcludeAssets>` and proves application code remains unambiguous.
- All successful output is deterministic and the verifier makes no network or model calls after package restore.

## Prerequisites

- .NET SDK 10.0.303 or a later compatible servicing SDK
- PowerShell 5.1 or newer to run the one-command verifier
- Network access only for the initial NuGet restore of `System.Linq.Async` 6.0.1

No credentials, database, service, or environment variables are required.

## Setup and verification

From this folder, run:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

The script restores all three projects, verifies formatting, builds and runs both fixes, then confirms that the collision probe fails for the intended reason.

To inspect each path separately:

```powershell
dotnet build .\CollisionProbe\CollisionProbe.csproj -c Release
dotnet run --project .\PlatformFix\PlatformFix.csproj -c Release
dotnet run --project .\TransitiveFix\TransitiveFix.csproj -c Release
```

The first command is expected to fail with `CS0121`. The two run commands must finish with `PASS 5/5` and `PASS 1/1` respectively.

## Expected behavior

The verifier ends with:

```text
PASS: no vulnerable packages in TransitiveFix.csproj
PASS: no vulnerable packages in CollisionProbe.csproj
PASS: CollisionProbe fails with CS0121 as expected
PASS verification 6/6
```

That gives the negative case a stable assertion instead of leaving a broken project unexplained.

## Limitations

This sample checks compile-time binding and deterministic enumeration behavior. It does not test every operator difference between the old package and the .NET 10 platform API. Before removing the package, audit calls such as `SelectAwait` and replace them with their current signatures. For projects that target both .NET 10 and earlier frameworks, follow Microsoft's guidance to reference `System.Linq.AsyncEnumerable` rather than applying this single-target layout unchanged.

See Microsoft's [breaking-change guidance](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/10.0/asyncenumerable) and the [.NET 10 `AsyncEnumerable` API](https://learn.microsoft.com/dotnet/api/system.linq.asyncenumerable?view=net-10.0).
