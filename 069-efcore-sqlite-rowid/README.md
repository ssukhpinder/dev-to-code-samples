# EF Core 10 SQLite ROWID keys

## Problem

By convention, EF Core's SQLite provider creates eligible integer primary keys
with `AUTOINCREMENT`. That prevents SQLite from reusing a ROWID that belonged to
a deleted row, but SQLite documents extra CPU, memory, disk, and I/O work for
that guarantee.

EF Core 10 can omit `AUTOINCREMENT` while keeping server-generated integer
keys. This sample compares both policies in one in-memory database and proves
the difference without relying on timing or a database server.

The executable verifies:

1. Both entity types begin with unset keys and receive keys from SQLite.
2. The conventional table DDL includes `AUTOINCREMENT`.
3. The configured table remains an `INTEGER PRIMARY KEY` without
   `AUTOINCREMENT`.
4. After deleting the current maximum key, `AUTOINCREMENT` advances while the
   default ROWID algorithm reuses that key.
5. Only the `AUTOINCREMENT` table is tracked in `sqlite_sequence`.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- NuGet access for the first restore of
  `Microsoft.EntityFrameworkCore.Sqlite` 10.0.11.
- No SQLite installation, database server, account, credential, connection
  string secret, or paid service.

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
PASS: both models start with unset keys
PASS: AUTOINCREMENT keys are database-generated
PASS: ROWID keys are database-generated
PASS: convention emits AUTOINCREMENT
PASS: configured table omits AUTOINCREMENT
PASS: ROWID table keeps an INTEGER PRIMARY KEY
PASS: AUTOINCREMENT does not reuse deleted key 3
PASS: ROWID reuses the deleted maximum key
PASS: sqlite_sequence tracks only the AUTOINCREMENT table
PASS: final key sets expose the reuse policy
PASS: 10/10 checks
```

All data and operations are fixed. The verifier uses a new in-memory database
on every run and prints no timestamps, paths, random values, or locale-sensitive
data, so repeated runs produce identical output.

## Configuration

EF Core 10 exposes the provider strategy through model metadata:

```csharp
modelBuilder.Entity<RowidItem>()
    .Property(item => item.Id)
    .Metadata.SetValueGenerationStrategy(SqliteValueGenerationStrategy.None);
```

`None` removes the SQLite-specific `AUTOINCREMENT` strategy. It does not turn
off key generation: an `INTEGER PRIMARY KEY` is still an alias for SQLite's
ROWID, and SQLite supplies a value when the insert omits the key.

This is different from `ValueGeneratedNever()`, which tells EF that the
application supplies the property value. Changing the SQLite column type from
`INTEGER` to `INT` also changes the contract and disables automatic ROWID-backed
generation.

## Limitations

- Default ROWID assignment can reuse deleted values; it does not promise that
  every gap will be filled. This sample deliberately deletes the current
  maximum to make the documented reuse behavior deterministic.
- Keep `AUTOINCREMENT` when identifiers must never be reused during the
  database's lifetime, including when IDs escape as durable public references.
- Primary keys are identifiers, not gap-free sequence numbers. Neither policy
  promises consecutive values after failed or rolled-back work.
- This is a behavior verifier, not a performance benchmark. Measure your own
  write workload before treating the documented overhead as material.

See the official [EF Core SQLite value-generation documentation](https://learn.microsoft.com/ef/core/providers/sqlite/value-generation),
[EF Core 10 release notes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew),
and [SQLite AUTOINCREMENT documentation](https://www.sqlite.org/autoinc.html)
for the provider configuration and database guarantees.
