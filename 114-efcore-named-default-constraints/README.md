# EF Core 10 named default constraints

## Problem

SQL Server generates opaque names when Entity Framework creates unnamed default
constraints. EF Core 10 adds `UseNamedDefaultConstraints()`, but enabling it on
an existing model makes the next migration touch every default constraint.

This sample keeps a baseline migration with three unnamed defaults and a second
migration generated after enabling the convention. Its offline verifier proves
that the second migration:

1. Changes all three existing modeled defaults.
2. Records the predictable names `DF_Jobs_Status`,
   `DF_Jobs_RetryCount`, and `DF_Jobs_CreatedUtc`.
3. Generates SQL that discovers and drops each database-generated constraint.
4. Recreates all three defaults with deterministic names.
5. Does not drop the table or any column.

No migration is applied, and the verifier never calls a database operation.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- NuGet access for the first restore of EF Core SQL Server and Design packages
  10.0.11 and the repo-local `dotnet-ef` 10.0.11 tool.
- No SQL Server installation, database, account, credential, or paid service.
  The `.invalid` connection string is a non-routable placeholder and remains
  closed during verification.

## Setup and validation

From this folder, run:

```powershell
dotnet tool restore
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

To print the reviewed upgrade SQL without applying it, run:

```powershell
dotnet tool run dotnet-ef migrations script InitialSchema NameDefaultConstraints --no-build --configuration Release
```

## Expected behavior

The executable is the deterministic verifier. It exits nonzero on the first
failed contract and ends with:

```text
PASS: sample contains the baseline and naming migrations
PASS: enabling the convention changes all three defaults
PASS: the migration records deterministic constraint names
PASS: migration SQL is deterministic across repeated generation
PASS: SQL discovers each database-generated constraint name
PASS: SQL drops all three existing default constraints
PASS: SQL recreates all three defaults with predictable names
PASS: preview contains no table or column drop
PASS: placeholder SQL Server connection remains closed
PASS: 9/9 checks
```

All assertions use committed migrations and generated SQL. The verifier prints
no timestamps, paths, random values, or locale-sensitive data, so repeated runs
produce identical output.

## The configuration change

The current model enables one global convention:

```csharp
protected override void OnModelCreating(ModelBuilder modelBuilder)
{
    modelBuilder.UseNamedDefaultConstraints();

    modelBuilder.Entity<Job>(entity =>
    {
        entity.Property(job => job.Status).HasDefaultValue("queued");
        entity.Property(job => job.CreatedUtc)
            .HasDefaultValueSql("SYSUTCDATETIME()");
        entity.Property(job => job.RetryCount).HasDefaultValue(0);
    });
}
```

Compare `InitialSchema` with `NameDefaultConstraints` in the `Migrations`
folder. The latter contains three `AlterColumn` operations annotated with the
new names. The generated SQL first queries `sys.default_constraints` because
the old names came from SQL Server, then drops and recreates the defaults.

For a narrower rollout, EF Core 10 also accepts an explicit constraint name in
`HasDefaultValue(...)` or `HasDefaultValueSql(...)`. That lets a migration name
selected defaults instead of enabling the convention for the whole model.

## Limitations

- Named default constraints and this generated SQL are SQL Server-specific.
- The verifier reviews migration operations and SQL; it deliberately does not
  apply them or estimate production lock duration.
- Dropping and recreating defaults can require a maintenance plan on a busy
  schema. Review generated SQL and test against a representative database.
- `UseNamedDefaultConstraints()` is intentionally global. Prefer explicit
  property-level names when a mass migration is too broad.

See the official [EF Core 10 release notes](https://learn.microsoft.com/ef/core/what-is-new/ef-core-10.0/whatsnew#custom-default-constraint-names),
[`UseNamedDefaultConstraints` API reference](https://learn.microsoft.com/dotnet/api/microsoft.entityframeworkcore.sqlservermodelbuilderextensions.usenameddefaultconstraints?view=efcore-10.0),
and [managing migrations guidance](https://learn.microsoft.com/ef/core/managing-schemas/migrations/managing)
for the feature, provider API, and migration-review workflow.
