# EF Core 10 named query filters

An admin recycle-bin query often needs to show soft-deleted rows for one tenant. Calling parameterless `IgnoreQueryFilters()` also disables the tenant filter, which can expose another tenant's data. EF Core 10 named query filters let a query disable the soft-delete rule without removing the tenant boundary.

## What this sample demonstrates

The sample defines two independent filters on `WorkItem`:

- `SoftDeletionFilter` hides rows marked as deleted.
- `TenantFilter` limits rows to the tenant supplied to the current `DbContext`.

It then verifies five query shapes against an in-memory SQLite database. The important comparison is between selectively disabling `SoftDeletionFilter` and calling parameterless `IgnoreQueryFilters()`.

## Prerequisites

- .NET 10 SDK
- Network access for the first NuGet restore

No API key, database server, paid service, or other credential is required. SQLite runs in memory and all fixture data is local.

## Setup and run

From this folder:

```bash
dotnet restore
dotnet run --configuration Release --no-restore
```

Expected behavior:

```text
PASS default query keeps both filters: [north-active]
PASS recycle bin disables only soft delete: [north-active, north-deleted]
PASS disabling only tenant filter still hides deleted rows: [north-active, south-active]
PASS parameterless IgnoreQueryFilters disables every boundary: [north-active, north-deleted, south-active, south-deleted]
PASS tenant value is scoped per DbContext: [south-active]
Verifier: 5/5 passed
```

The process exits with code `0` only when all five checks pass.

## Deterministic verification

Run the same checks used for this sample:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --project EfCoreNamedQueryFilters.csproj --configuration Release --no-build
dotnet list package
dotnet list package --vulnerable --include-transitive
```

The database is recreated in memory on every run. The queries sort by title before comparing fixed expected arrays, so the verifier does not depend on provider row order, clock time, locale, or external state.

## Limitations

Named filters make selective disabling possible; they do not create an authorization boundary by themselves. Keep tenant identity server-controlled, review every query that disables a filter, and enforce isolation in the database too when the risk warrants row-level security. This small sample also does not cover required-navigation behavior, which can cause filtered parents or children to disappear through inner joins.
