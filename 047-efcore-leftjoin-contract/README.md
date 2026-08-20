# EF Core 10 LeftJoin contract

The pre-.NET 10 left-outer-join pattern requires `GroupJoin`, `SelectMany`, and `DefaultIfEmpty` in a shape that the query provider recognizes. A small refactor can make that expression harder to review or stop it from translating as intended. .NET 10 adds first-class `LeftJoin` and `RightJoin` operators, and EF Core 10 translates them directly.

## What this sample demonstrates

The sample creates a fixed in-memory SQLite database with:

- one employee matched to a department;
- one employee whose department is missing; and
- one department with no employee.

It runs `LeftJoin` and `RightJoin`, inspects the generated SQL, and verifies that the unmatched rows survive with explicit fallback values. This catches an accidental inner join as both a SQL-shape failure and a result-set failure.

## Prerequisites

- .NET 10 SDK
- Network access for the first NuGet restore

No API key, database server, paid service, credential, or credential placeholder is required. SQLite runs in memory and all fixture data is local.

## Setup and run

From this folder:

```bash
dotnet restore
dotnet run --configuration Release --no-restore
```

Expected behavior:

```text
PASS LeftJoin translates to LEFT JOIN
PASS LeftJoin keeps the matched employee
PASS LeftJoin keeps the orphaned employee
PASS LeftJoin returns the expected rows
PASS RightJoin translates to RIGHT JOIN
PASS RightJoin keeps the unstaffed department
PASS RightJoin returns the expected rows
Verifier: 7/7 passed
```

The process exits with code `0` only when all seven checks pass.

## Deterministic verification

Run the complete validation used for this sample:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --project EfCoreLeftJoinContract.csproj --configuration Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The database is recreated in memory on every run. Queries have explicit ordering, expected values are fixed, and no check depends on provider row order, network data, clock time, locale, randomness, or external state.

## Limitations

`LeftJoin` and `RightJoin` are equality joins; they do not replace every correlated subquery or provider-specific join. C# query syntax does not yet have dedicated left/right join clauses, so these operators use method syntax. Translation is also provider-specific: keep a test against the database provider and version that production uses. A captured SQL string is useful as a focused contract, but avoid snapshotting every alias or whitespace detail.
