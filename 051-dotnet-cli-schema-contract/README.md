# .NET 10 CLI schema contract test

Scripts and editor integrations often scrape `dotnet --help` output or assume that an option keeps the same alias, type, and arity. Human-readable help is the wrong contract for a machine. .NET 10 adds `--cli-schema`, which returns the invoked command tree as JSON.

This sample captures `dotnet build --cli-schema` and checks only the semantics that a hypothetical wrapper consumes. It also removes the `-c` alias in memory and proves that the same verifier rejects that drift.

## What this sample demonstrates

- Launch `dotnet build --cli-schema` without a shell and parse its JSON output.
- Require .NET 10 and the `build` command while ignoring descriptions and option order.
- Verify the aliases, value types, and arities used by a wrapper: `--configuration`/`-c`, `--framework`/`-f`, and `--no-restore`.
- Accept additive CLI changes because the verifier does not snapshot the complete document.
- Run a negative control that deletes `-c` and must fail exactly that contract check.

## Prerequisites

- .NET 10 SDK
- Windows PowerShell 5.1 or PowerShell 7+

No API key, service credential, paid API, package dependency, or runtime network call is required. `global.json` selects the latest installed .NET 10 feature band and excludes prerelease SDKs.

## Setup and deterministic verification

From this folder on Windows, run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\verify.ps1
```

On macOS or Linux with PowerShell 7, run:

```powershell
pwsh -NoProfile -NonInteractive -File ./verify.ps1
```

The script restores, verifies formatting, builds in Release mode, runs the verifier five times, requires byte-identical output, lists dependencies, and audits vulnerable packages.

Expected final output includes:

```text
PASS command name is build
PASS schema reports a .NET 10 SDK
PASS --configuration keeps the -c alias
PASS --no-restore accepts no value
PASS negative control rejects a missing -c alias
Verifier: 12/12 passed
PASS repeated verifier output was identical across 5 runs
Validation complete.
```

## Run the contract check directly

```bash
dotnet restore CliSchemaContract.csproj
dotnet run --project CliSchemaContract.csproj --configuration Release
```

A failed semantic check produces a nonzero exit code, so the verifier can be used as a CI gate around a CLI-dependent tool.

## Complete validation commands

```powershell
dotnet restore .\CliSchemaContract.csproj
dotnet format whitespace .\CliSchemaContract.csproj --verify-no-changes --no-restore
dotnet build .\CliSchemaContract.csproj --configuration Release --no-restore
dotnet run --project .\CliSchemaContract.csproj --configuration Release --no-build
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\verify.ps1
dotnet package list --project .\CliSchemaContract.csproj --include-transitive
dotnet package list --project .\CliSchemaContract.csproj --vulnerable --include-transitive
```

## Limitations

This is a consumer contract, not a promise that every field in the CLI schema is frozen. It deliberately ignores descriptions, ordering, hidden options, unrelated options, and additive changes. A real integration should assert only what it reads and update its contract deliberately when adopting a new SDK major version. The live check requires a local .NET 10 SDK; it does not prove behavior for SDKs that are not installed.
