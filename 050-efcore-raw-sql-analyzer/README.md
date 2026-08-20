# EF Core 10 raw SQL analyzer gate

String concatenation inside `FromSqlRaw`, `ExecuteSqlRaw`, and related raw SQL calls can turn a trusted query template into an injection boundary. EF Core 10 adds an analyzer for recognizable concatenation at those call sites. This sample promotes its `EF1003` diagnostic to an error and proves the gate with an intentionally failing project.

## What this sample demonstrates

- `UnsafeProbe` concatenates a column name inside `FromSqlRaw`. Its project promotes `EF1003` to an error, so a successful validation requires that project to fail for the expected reason.
- `SafeDemo` uses `FromSql(FormattableString)` for an untrusted value, which EF converts to a database parameter.
- When a column identifier must vary, `SafeDemo` selects one complete, hard-coded SQL template from an enum allowlist and passes the value separately to `FromSqlRaw`.
- An in-memory SQLite database proves that malicious-looking input cannot widen the result set and that the allowlisted query returns the expected rows.

## Prerequisites

- .NET 10 SDK
- Windows PowerShell 5.1 or PowerShell 7+
- Network access for the first NuGet restore

No database server, API key, paid service, or other credential is required. SQLite runs in memory. If a production sample needs a connection string, use a secret store or an environment variable such as `ConnectionStrings__Catalog`; never commit the value.

## Setup and deterministic verification

From this folder on Windows, run:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\verify.ps1
```

On macOS or Linux with PowerShell 7, run:

```powershell
pwsh -NoProfile -NonInteractive -File ./verify.ps1
```

The script restores both projects, verifies whitespace formatting, builds and runs the safe project, then builds the unsafe probe and requires a nonzero exit with `EF1003`.

Expected final output includes:

```text
PASS FromSql keeps the value in a parameter
PASS malicious-looking input matches no rows
PASS the injected OR clause never changes the result set
PASS the allowlisted category template returns both kitchen products
PASS the allowlisted raw template still parameterizes its value
PASS an unsupported identifier cannot reach a raw SQL template
Verifier: 6/6 passed
PASS UnsafeProbe failed with EF1003 as required.
Verifier: safe runtime checks 6/6; analyzer gate 1/1 passed.
```

## Run the safe demonstration directly

```bash
dotnet restore SafeDemo/SafeDemo.csproj
dotnet run --project SafeDemo/SafeDemo.csproj --configuration Release --no-restore
```

`SafeDemo` is the application pattern to copy. `UnsafeProbe` is a negative build fixture and is not intended to run.

## Complete validation commands

```powershell
dotnet restore .\SafeDemo\SafeDemo.csproj
dotnet restore .\UnsafeProbe\UnsafeProbe.csproj
dotnet format whitespace .\SafeDemo\SafeDemo.csproj --verify-no-changes --no-restore
dotnet format whitespace .\UnsafeProbe\UnsafeProbe.csproj --verify-no-changes --no-restore
dotnet build .\SafeDemo\SafeDemo.csproj --configuration Release --no-restore
dotnet run --project .\SafeDemo\SafeDemo.csproj --configuration Release --no-build
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File .\verify.ps1
dotnet package list --project .\SafeDemo\SafeDemo.csproj --include-transitive
dotnet package list --project .\SafeDemo\SafeDemo.csproj --vulnerable --include-transitive
dotnet package list --project .\UnsafeProbe\UnsafeProbe.csproj --include-transitive
dotnet package list --project .\UnsafeProbe\UnsafeProbe.csproj --vulnerable --include-transitive
```

## Limitations

The analyzer recognizes dangerous syntax at raw SQL call sites; it is not a taint-analysis engine and cannot prove that a string assembled elsewhere is safe. SQL identifiers cannot be parameterized, so use a closed allowlist of complete SQL templates and keep user input in parameter slots. Provider quoting rules differ, and this sample validates SQLite only. Keep provider-specific integration tests for the SQL shapes used in production.
