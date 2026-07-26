# QUERY Is a Real HTTP Method Now

Self-contained ASP.NET Core 10 demo of RFC 10008's `QUERY` method: a safe, idempotent, cacheable request that carries a body. One minimal API exposes the same product search as `GET`, `POST` and `QUERY`, then the app runs an `HttpClient` harness against itself and prints what actually happened.

📖 Article: _link added after publish_

## Run it

```bash
cd QueryMethodDemo
dotnet run -c Release
```

Requires the .NET 10 SDK (targets `net10.0`, no external packages).

## What it shows

1. **URL length wall** — filters in a query string survive 500 SKUs (7,534-byte request line) and get `414 RequestUriTooLong` at 600. The same 5,000 filters go through as a 65 KB `QUERY` body without complaint.
2. **Routing** — there is no `MapQuery`, so `MapMethods("/products/search", [HttpMethods.Query], …)` is the way in. `HttpMethod.Query` and `HttpMethods.IsQuery` both ship in .NET 10.
3. **Content-Type is mandatory** — RFC 10008 says the server MUST fail a `QUERY` with a missing or inconsistent `Content-Type`. That check is yours to write: the demo returns `400` for a missing type and `415` for `text/plain`.
4. **Output caching ignores QUERY** — `.CacheOutput()` caches `GET` and skips `QUERY`, even though the RFC calls its responses cacheable.
5. **The footgun** — a custom `IOutputCachePolicy` that opts `QUERY` in *looks* like the fix, and immediately serves a 4-SKU query the answer to a 2-SKU query. The output cache key is built from the URL, and for `QUERY` the URL is not the query.
