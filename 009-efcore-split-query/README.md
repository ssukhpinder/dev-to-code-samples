# EF Core Cartesian Explosion vs AsSplitQuery

Two sibling collection `Include`s on one EF Core query make the database JOIN
both collections into a single result set — every review row repeated once per
image, every image row repeated once per review. For a catalog of 12 products
(30 reviews + 8 images each, 468 entities total) the default single query reads
**2,880 rows and ~1 MB**; `AsSplitQuery()` reads **468 rows and ~69 KB** across
3 statements; a projection reads **12 rows**.

The demo measures, for each shape:

- SQL statements executed (captured with a `DbCommandInterceptor`)
- rows / cells / approximate payload the database returns (captured SQL replayed on a raw SQLite connection)
- median wall time and allocated bytes

It also surfaces the `MultipleCollectionIncludeWarning` EF Core has been
logging about this all along.

## Run it

```bash
dotnet run -c Release
dotnet run -c Release -- --sql   # also dump the captured SQL per scenario
```

Requires the .NET 10 SDK. Uses a local SQLite file (`catalog.db`), recreated on
every run.

📖 Article: [Two Includes, 2,880 Rows: EF Core's Quiet Cartesian Tax](https://dev.to/ssukhpinder/two-includes-2880-rows-ef-cores-quiet-cartesian-tax-2b9a)
