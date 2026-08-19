# EF Core 10 parameterized collections

## Problem

EF Core 10 changed the default SQL translation for parameterized collections such as `ids.Contains(entity.Id)`. Instead of one JSON collection parameter, the SQL Server provider now emits multiple scalar parameters and may pad the parameter count to reuse more query plans.

That default gives the optimizer collection-cardinality information, but it also changes the SQL shape during an EF Core 8 or 9 upgrade. Workloads that depended on `OPENJSON`, plan-cache behavior, or a particular cardinality estimate need an explicit regression check.

This sample generates SQL without connecting to a database and verifies four choices:

- the EF Core 10 multiple-parameter default;
- `EF.Parameter`, which requests the previous single JSON parameter shape;
- `EF.Constant`, which inlines the values;
- `EF.MultipleParameters`, which makes the new strategy explicit.

It also proves the documented padding behavior: a collection with eight values produces a ten-parameter SQL shape.

## Prerequisites

- .NET 10 SDK
- Internet access for the initial NuGet restore

SQL Server and credentials are not required. `ToQueryString()` translates the LINQ expressions but does not execute them.

## Setup and run

From this folder:

```powershell
dotnet restore EfCoreParameterizedCollections.csproj
dotnet format EfCoreParameterizedCollections.csproj --verify-no-changes --no-restore
dotnet build EfCoreParameterizedCollections.csproj --configuration Release --no-restore
dotnet run --project EfCoreParameterizedCollections.csproj --configuration Release --no-build
```

The verifier prints the generated SQL and ends with:

```text
PASS: EF Core 10 default uses multiple scalar parameters
PASS: EF.Parameter restores one JSON collection parameter
PASS: EF.Constant inlines this collection
PASS: EF.MultipleParameters makes the scalar strategy explicit
PASS: Eight values are padded to a ten-parameter SQL shape
All checks passed: 5/5
```

## Deterministic verification

The run is offline after restore. It does not make a database connection, seed data, call a model, or require environment variables.

Optional dependency checks:

```powershell
dotnet list EfCoreParameterizedCollections.csproj package --include-transitive
dotnet list EfCoreParameterizedCollections.csproj package --vulnerable --include-transitive
```

The project pins `Microsoft.EntityFrameworkCore.SqlServer` 10.0.11, the stable servicing release used by the verifier.

## Expected behavior and limitations

`ToQueryString()` is useful for detecting a translation change before deployment. It is not a query-plan benchmark. Provider version, database statistics, collection sizes, and workload shape still determine whether multiple parameters, one JSON parameter, or constants perform best.

Before changing the global translation mode, compare representative queries against a production-like database and inspect execution plans, plan-cache behavior, and latency. A per-query `EF.Parameter`, `EF.Constant`, or `EF.MultipleParameters` override keeps the decision close to the query that needs it.

Primary references:

- [EF Core 10 breaking change: parameterized collections](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/breaking-changes#parameterized-collections-now-use-multiple-parameters-by-default)
- [What's new in EF Core 10](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew#improved-translation-for-parameterized-collection)
