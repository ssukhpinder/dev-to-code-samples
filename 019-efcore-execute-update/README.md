# ExecuteUpdate vs load-modify-save

What a set-based `ExecuteUpdate` actually saves compared to the classic
load-the-entities-flip-the-flag-`SaveChanges()` pattern, measured instead of
guessed: a nightly archive job over 20,000 seeded orders (8,956 matching), with
a `DbCommandInterceptor` counting every SQL command EF Core sends.

Also demonstrates:

- the exact single UPDATE statement EF generates for `ExecuteUpdate`
- the stale-tracker gotcha (tracked entities don't see set-based updates)
- an `ExecuteDelete` cameo

## Run it

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK. Uses EF Core 10 with SQLite (a local `orders.db`
file is created next to the binary; deleted and reseeded on every run).

📖 Article: _link added after publish_
