# One Cache Miss, Fifty Database Calls

Self-contained ASP.NET Core demo of a cache stampede: 50 concurrent requests hit a cold cache key, `IMemoryCache.GetOrCreateAsync` runs the database query 50 times, `HybridCache` runs it once. The app starts a minimal API on a random port, hammers its own endpoints with `HttpClient`, and prints how many times the fake database actually got called.

📖 Article: [One Cache Miss, Fifty Database Calls](https://dev.to/ssukhpinder/one-cache-miss-fifty-database-calls-1k5n)

## Run it

```bash
cd StampedeDemo
dotnet run -c Release
```

Requires the .NET 10 SDK (targets `net10.0`, uses `Microsoft.Extensions.Caching.Hybrid` 10.0.0).

## What it shows

1. **Cold burst** — 50 concurrent requests for the same uncached key: ~50 db calls via `IMemoryCache`, exactly 1 via `HybridCache`
2. **Warm burst** — same 50 requests once the key is cached: 0 db calls either way
3. **Bonus footgun** — `HybridCache`'s L1 *is* the DI-registered `MemoryCache`, so reusing the same raw key across both APIs throws `InvalidCastException` (hence the `mem:`/`hyb:` prefixes in the code)
