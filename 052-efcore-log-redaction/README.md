# EF Core 10 inlined-constant log redaction

## Problem

`EF.Constant` deliberately places a value inside generated SQL instead of sending it as a parameter. That can help with a measured query-plan problem, but it also means the value is part of the command text. Before EF Core 10, the same value could appear in normal SQL logs.

EF Core 10 keeps executing the real command while replacing inlined values with `?` in its default log message. This sample proves that boundary with two fake role names. It also shows that `EnableSensitiveDataLogging()` deliberately restores the values to the log.

## Prerequisites

- .NET 10 SDK. The sample was verified with SDK 10.0.303.
- Network access for the initial NuGet restore only.
- No database server, credential, or external service.

The project pins `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11 and uses an in-memory SQLite connection.

## Setup and run

From this folder:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
```

Expected output:

```text
PASS: default query returns both selected roles
PASS: default log uses redaction markers
PASS: default log hides the first role
PASS: default log hides the second role
PASS: sensitive query returns the same rows
PASS: sensitive log shows the first role
PASS: sensitive log shows the second role
Summary: 7/7 checks passed
```

The verifier captures only `RelationalEventId.CommandExecuted`. It clears setup messages before querying, so seed commands cannot produce a false result.

## Deterministic verification

Run formatting, build, and the verifier:

```bash
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

Repeat the executable if you want to confirm stable output:

```powershell
1..5 | ForEach-Object { dotnet run -c Release --no-build }
```

The sample exits with code `0` only when all seven checks pass.

## Expected behavior and limits

- With default logging, SQLite's logged `IN` list is `IN (?, ?)` and neither fake role appears.
- With sensitive-data logging enabled, both role values appear, while the returned rows stay identical.
- SQL formatting and redaction markers are provider-specific. Use equivalent assertions for the provider deployed by your application.
- This protects EF's log representation. It does not scrub arbitrary application logs, database-server logs, raw SQL literals, or custom interceptors that inspect the executable `DbCommand`.
- `EnableSensitiveDataLogging()` is included only to prove the opt-in boundary. Do not feed it real secrets or enable it casually in production.
