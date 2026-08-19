# Microsoft.Testing.Platform Exit Code 5: Route Filters in Mixed Test Solutions

A mixed Microsoft.Testing.Platform solution can reject a framework-specific command-line
option that is valid for one project but unknown to another. This sample reproduces exit code
5 with MSTest and xUnit.net, then routes each framework's filter through conditional MSBuild
properties.

## Problem

The solution contains two .NET 10 test projects:

- `MSTestSpecs` uses `MSTest.Sdk` and understands `--filter`.
- `XunitSpecs` uses xUnit.net v3 and understands `--filter-trait`.

Passing `--filter-trait` to the whole solution also sends it to MSTest. MSTest rejects the
unknown option, so Microsoft.Testing.Platform returns exit code 5 for invalid arguments.
Ignoring that code would hide a configuration bug and may leave part of the solution untested.

## Prerequisites and setup

- .NET 10 SDK
- PowerShell 7 or Windows PowerShell 5.1 for the verifier
- No credentials or runtime external services; the first restore needs NuGet access unless the packages are cached

Restore and build from this folder:

```powershell
dotnet restore MixedTesting.slnx
dotnet build MixedTesting.slnx --configuration Release --no-restore
```

## Reproduce exit code 5

This xUnit-specific filter is sent to both projects:

```powershell
dotnet test --solution MixedTesting.slnx --configuration Release --no-build --no-restore --filter-trait Category=Integration
```

The xUnit project accepts the option. The MSTest project reports an unrecognized argument, and
the command exits with code 5.

## Route each filter to the right project

`Directory.Build.props` appends the MSTest arguments only when `UsingMSTestSdk` is `true`. The
other project receives the xUnit arguments:

```xml
<TestingPlatformCommandLineArguments Condition="'$(UsingMSTestSdk)' == 'true'">
  $(TestingPlatformCommandLineArguments) $(MSTestSpecificArgs)
</TestingPlatformCommandLineArguments>
<TestingPlatformCommandLineArguments Condition="'$(UsingMSTestSdk)' != 'true'">
  $(TestingPlatformCommandLineArguments) $(XUnitSpecificArgs)
</TestingPlatformCommandLineArguments>
```

Run the corrected solution command:

```powershell
dotnet test --solution MixedTesting.slnx --configuration Release --no-build --no-restore `
  "-p:MSTestSpecificArgs=--filter TestCategory=Integration" `
  "-p:XUnitSpecificArgs=--filter-trait Category=Integration"
```

For deterministic verification, run:

```powershell
# PowerShell 7 on Windows, Linux, or macOS
pwsh -NoProfile -File ./verify.ps1

# Windows PowerShell 5.1
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

Expected output includes:

```text
PASS: unscoped xUnit filter returned exit code 5
PASS: routed filters returned exit code 0
PASS: exactly two integration tests ran
```

## Limitations

`UsingMSTestSdk` is supplied by `MSTest.Sdk`. For another test framework or extension, use a
property that its package defines or add an explicit marker property to the relevant projects.
Shared platform options can remain unconditional. Framework-specific options need their own
routing rule, and a new test project should be covered by that rule before it joins CI.
