# .NET 10 ISO week grouping with `DateOnly`

## Problem

An ISO week can cross a Gregorian year boundary. Pairing `ISOWeek.GetWeekOfYear(date)` with `date.Year` can therefore put dates almost a year apart into the same reporting bucket. For example, both 2025-01-01 and 2025-12-29 produce the misleading key `2025-W01` when the Gregorian year is used.

This sample uses the .NET 10 `DateOnly` overloads on `ISOWeek` to:

- build keys from the ISO week-numbering year and ISO week number;
- reconstruct the exact Monday-to-Sunday range with `ISOWeek.ToDateOnly`;
- expose the incorrect Gregorian-year grouping with fixed ledger entries; and
- round-trip every fixture date through its ISO week parts.

## Prerequisites

- .NET 10 SDK
- No credentials, network service, database, or paid API

## Setup and validation

Run these commands from this folder:

```text
dotnet restore
dotnet format --verify-no-changes
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The project has no package dependencies. The last two commands make that explicit and keep the validation sequence compatible with samples that do use packages.

## Expected behavior

The program first prints the correctly separated ISO groups:

```text
Correct ISO groups:
2025-W01: 2025-01-01 total=5.00
2026-W01: 2025-12-29, 2026-01-04 total=50.00
2026-W02: 2026-01-05 total=40.00
2026-W53: 2027-01-01 total=50.00
```

It then runs six deterministic checks. The important comparison proves that `DateOnly.Year` plus the ISO week number incorrectly joins 2025-01-01 and 2025-12-29, while `ISOWeek.GetYear(date)` keeps the two week-numbering years separate.

A successful run ends with:

```text
Verifier: 6/6 passed
```

The process exits with code `1` if any check fails, so the same command can be used in CI.

## Limitations

This sample implements ISO 8601 Monday-to-Sunday week grouping. It does not model locale-specific week rules, fiscal calendars, time zones, or database translation. Convert timestamps to the intended business date before creating an ISO week key, and use a separate calendar abstraction when the reporting year is not ISO-based.
