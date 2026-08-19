# .NET 10 `dotnet tool exec` version pinning

This sample shows why a one-shot .NET tool command should pin both the package version and its NuGet source in CI. It builds two versions of the same tiny tool into a local feed, proves that an unpinned invocation selects the newest version, proves that `@1.0.0` selects exactly version 1.0.0, and verifies that the tool's nonzero exit code reaches the caller.

## Prerequisites

- .NET 10.0.100 SDK or later
- Windows PowerShell 5.1 or PowerShell 7+

No credentials, external services, or third-party packages are required. All package restore and tool execution use the local `artifacts/packages` feed configured by `NuGet.Config`.

## Run the deterministic verification

From this folder on Windows, run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\verify.ps1
```

On macOS or Linux with PowerShell 7, run:

```powershell
pwsh -NoProfile -NonInteractive -File ./verify.ps1
```

The script performs these steps:

1. Safely clears only this sample's generated `artifacts` directory, then restores and packs `Sukhpinder.DevTo.ToolExec.Demo` as versions `1.0.0` and `2.0.0`.
2. Runs the exact pin with `dotnet tool exec ...@1.0.0` and checks the reported assembly version.
3. Runs the package without a version and proves the local feed resolves `2.0.0`.
4. Runs version `1.0.0` with `--exit-code 23` and checks that `dotnet tool exec` returns `23`.

Expected final output:

```text
PASS exact @1.0.0 pin selected version 1.0.0
PASS unpinned invocation selected the newest available version
PASS dotnet tool exec propagated tool exit code 23
3/3 deterministic checks passed.
```

## Run the pinned command directly

After the verification script has created the local packages, this is the important command:

```powershell
dotnet tool exec `
  --configfile ./NuGet.Config `
  --source ./artifacts/packages `
  Sukhpinder.DevTo.ToolExec.Demo@1.0.0 `
  -- --label ci
```

Everything after `--` belongs to the tool. `dotnet tool exec` returns the tool's exit code, so an ordinary failing tool can fail a CI step without a wrapper-specific convention.

## Files

- `src/DemoTool/DemoTool.csproj` declares a packable .NET tool with no package dependencies.
- `src/DemoTool/Program.cs` prints its assembly version and optionally returns a requested exit code.
- `NuGet.Config` clears inherited feeds, maps the demo package to one local feed, and keeps the package cache under ignored `artifacts` output.
- `verify.ps1` packages both versions and checks version selection plus exit-code propagation.

## Limitations

The local feed makes the sample deterministic; a production feed still needs normal credential, availability, package-signing, and supply-chain controls. An exact version pin prevents surprise upgrades, but it does not prove that a package is trustworthy. If a repository already requires an auditable local tool manifest, `dotnet tool restore` plus `dotnet tool run` may be the clearer policy.
