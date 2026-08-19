# Microsoft.Data.Sqlite 10 UTC handling

## Problem

Microsoft.Data.Sqlite 10 changed three high-impact date/time behaviors to remove dependence on the machine's local time zone:

- `GetDateTimeOffset` treats offsetless `TEXT` timestamps as UTC;
- `GetDateTime` converts offset-bearing `TEXT` timestamps to UTC and returns `DateTimeKind.Utc`; and
- a `DateTimeOffset` bound as `REAL` is normalized to UTC before it is stored.

An application that previously treated an offsetless value such as `2014-04-15 10:47:16` as local time can therefore observe a different instant after upgrading. The original offset is not present in that value, so the provider cannot recover it.

This sample creates an in-memory SQLite database, verifies all three version-10 behaviors, and runs a small audit for parseable `TEXT` timestamps that have no `Z` or numeric offset. The audit flags the rows that require a domain decision before an upgrade.

## Prerequisites

- .NET 10 SDK
- network access for the first NuGet restore

No API key, database server, account, paid service, or other credential is required. The database exists only in memory.

## Setup

From this folder:

```powershell
dotnet restore .\SqliteUtcHandling.csproj --nologo
dotnet build .\SqliteUtcHandling.csproj --configuration Release --no-restore --nologo
```

The project pins the stable `Microsoft.Data.Sqlite` package at `10.0.11`.

## Run and verify

Run the executable verifier:

```powershell
dotnet run --project .\SqliteUtcHandling.csproj --configuration Release --no-build
```

Expected output:

```text
PASS: offsetless TEXT was interpreted as UTC
PASS: offset-bearing TEXT was converted to a UTC DateTime
PASS: DateTimeOffset written to REAL was normalized to UTC
PASS: audit found only parseable offsetless TEXT timestamps
4/4 checks passed
```

## Deterministic validation

Run all sample gates with:

```powershell
dotnet restore .\SqliteUtcHandling.csproj --nologo
dotnet format .\SqliteUtcHandling.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build .\SqliteUtcHandling.csproj --configuration Release --no-restore --nologo
dotnet run --project .\SqliteUtcHandling.csproj --configuration Release --no-build
dotnet list .\SqliteUtcHandling.csproj package --include-transitive
dotnet list .\SqliteUtcHandling.csproj package --vulnerable --include-transitive
```

The sample uses fixed timestamp values, an in-memory database, invariant parsing, and exact assertions. It exits nonzero on the first unexpected value, offset, `DateTimeKind`, or audit result.

## Expected behavior and limitations

The verifier expects offsetless `TEXT` to produce `+00:00`, an explicit `+02:00` value to become the corresponding UTC `DateTime`, and a `DateTimeOffset` bound as `REAL` to round-trip as the same instant at offset zero. The audit must return only rows `1` and `4`.

The audit recognizes four common ISO-like offsetless formats used by this fixture. It is not a universal SQLite date parser; extend its accepted formats to match the schema and data conventions you actually own. It also cannot determine which time zone an old offsetless value was intended to represent. That decision requires application context and may require a data migration.

Microsoft documents the process-wide `Microsoft.Data.Sqlite.Pre10TimeZoneHandling` switch as a temporary last resort. This sample does not enable it because retaining machine-local interpretation would hide the rows that need review. It also does not cover EF Core property converters, Unix timestamps stored as `INTEGER`, daylight-saving transitions, or production backup and migration procedures.

Primary references:

- [Microsoft.Data.Sqlite 10 breaking changes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#using-getdatetimeoffset-without-an-offset-now-assumes-utc)
- [SQLite data types in Microsoft.Data.Sqlite](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/types)
- [Microsoft.Data.Sqlite parameters](https://learn.microsoft.com/en-us/dotnet/standard/data/sqlite/parameters)
