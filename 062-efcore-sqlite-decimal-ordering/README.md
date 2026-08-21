# EF Core 10 SQLite decimal ordering

## Problem

SQLite has no native `decimal` storage class, so the EF Core SQLite provider
maps .NET decimal values to `TEXT`. Plain SQLite text ordering puts values such
as `10.00` before `2.50`, while older EF Core versions could not translate
`ORDER BY`, `MIN`, or `MAX` over a decimal property.

EF Core 10 adds server-side support for these operations. It registers the
`EF_DECIMAL` collation for ordering and the `ef_min` and `ef_max` aggregate
functions for decimal bounds. This sample proves that contract without a
database server.

The executable creates an in-memory database with fixed prices and verifies:

1. The underlying SQLite values are still stored as `TEXT`.
2. Plain SQLite text order differs from numeric order.
3. EF Core emits `COLLATE EF_DECIMAL` and returns numeric ascending and
   descending order.
4. Decimal `MIN` and `MAX` stay in SQL through `ef_min` and `ef_max`.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- NuGet access for the first restore of
  `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11.
- No SQLite installation, database server, credential, or connection-string
  secret.

## Setup and validation

From this folder, run:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The executable is the deterministic verifier. It exits nonzero on the first
failed contract.

## Expected behavior

The run ends with:

```text
PASS: seeded five fixed prices
PASS: SQLite stores every decimal as TEXT
PASS: plain SQLite TEXT ordering is not numeric ordering
PASS: EF query uses the decimal collation
PASS: ascending prices are numeric
PASS: descending prices are numeric
PASS: minimum stays in SQL
PASS: maximum stays in SQL
PASS: minimum price is correct
PASS: maximum price is correct
PASS: 10/10 checks
```

All inputs are fixed. The verifier uses an in-memory database and prints no
timestamps, generated IDs, environment-dependent paths, or locale-dependent
decimal values, so repeated runs produce identical output.

## Why I keep the property as decimal

Changing a price to `double` just to make an older query translate trades a
visible provider limitation for binary floating-point behavior. Pulling rows
into memory with `AsEnumerable` also changes where sorting happens and can make
the application read far more data than intended.

With EF Core 10, I keep the domain property as `decimal` and test the generated
SQL contract. `ToQueryString()` makes the provider behavior visible without
depending on log formatting.

## Limitations

- This verifies `ORDER BY`, `MIN`, and `MAX`, the decimal operations added for
  SQLite in EF Core 10. It does not imply that every decimal operation has a
  native SQLite translation.
- `EF_DECIMAL`, `ef_min`, and `ef_max` are registered by the EF Core provider.
  Raw SQLite connections and external SQLite tools do not gain them
  automatically.
- The sample is a correctness regression test, not a performance benchmark.

See the official [EF Core 10 release notes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew#other-query-improvements)
and the [SQLite provider function mappings](https://learn.microsoft.com/ef/core/providers/sqlite/functions)
for the supported translation contract.
