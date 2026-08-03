# 021 — The Feature Flag That Needed a Restart

`IOptions<T>` vs `IOptionsSnapshot<T>` vs `IOptionsMonitor<T>` in ASP.NET Core (.NET 10), demonstrated live.

A minimal API reads the same `Features` section through all three interfaces, plus a singleton
that froze `IOptionsMonitor.CurrentValue` in its constructor (the sneaky one). Edit
`appsettings.json` while the app is running and watch which endpoints notice:

- `/options` — `IOptions<T>`: stale forever
- `/snapshot` — `IOptionsSnapshot<T>`: fresh (re-bound per request)
- `/monitor` — `IOptionsMonitor<T>`: fresh (change tokens), fires `OnChange`
- `/frozen` — monitor injected, but `CurrentValue` captured at construction: stale forever
- `/bench` — rough cost of per-scope re-binding vs `CurrentValue` reads

## Run it

```bash
dotnet run -c Release --urls http://localhost:5099
# in another terminal:
curl localhost:5099/options ; curl localhost:5099/snapshot ; curl localhost:5099/monitor ; curl localhost:5099/frozen
# flip "ExportEnabled" in appsettings.json, save, then curl the endpoints again
curl localhost:5099/bench
```

📖 Article: _link added after publish_
