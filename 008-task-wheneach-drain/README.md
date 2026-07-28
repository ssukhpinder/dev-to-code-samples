# The WhenAny Drain Loop Can Finally Retire

Compares three ways to consume a batch of in-flight tasks in .NET 10:

- `Task.WhenAll` — everything waits for the slowest task
- the classic `Task.WhenAny` + `List.Remove` drain loop — processes results as they finish, but is quietly quadratic (measured here: ~110 MB allocated to drain 5,000 tasks)
- `Task.WhenEach` (.NET 9+) — same completion-order behavior, ~1.3 MB

Also shows how much earlier a faulted task becomes visible with `WhenEach` versus `WhenAll`.

## Run it

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK.

📖 Article: _link added after publish_
