# Does Your BackgroundService Still Block Startup? (.NET 8 vs .NET 10)

Self-contained demo that measures when Kestrel actually starts answering while a hosted service does ~3.2 s of synchronous warmup. One minimal API + one warmup service, four modes selected via `WARMUP_MODE`:

1. `blocking` — `BackgroundService` whose `ExecuteAsync` does the warmup synchronously (no `await` before the work)
2. `yield` — same service with `await Task.Yield()` as the first line (the classic fix)
3. `concurrent` — same blocking service plus `HostOptions.ServicesStartConcurrently = true`
4. `startasync` — a plain `IHostedService` doing the same work inside `StartAsync`

The app starts Kestrel on `127.0.0.1:5199`, probes itself with `HttpClient`, and prints a timeline: process start → warmup start/finish → `ApplicationStarted` → first HTTP response → cache warm.

The project multi-targets `net8.0` and `net10.0` because the results differ: on .NET 8 a synchronous `ExecuteAsync` delays Kestrel by the full warmup time; on .NET 10 it doesn't, because `BackgroundService.StartAsync` now wraps `ExecuteAsync` in `Task.Run`. A synchronous `StartAsync` on a plain `IHostedService` still blocks on both.

📖 Article: _link added after publish_

## Run it

```bash
cd WarmupDemo
WARMUP_MODE=blocking dotnet run -c Release -f net10.0
WARMUP_MODE=blocking dotnet run -c Release -f net8.0
WARMUP_MODE=startasync dotnet run -c Release -f net10.0
```

Requires the .NET 10 SDK (plus the ASP.NET Core 8 runtime for the `net8.0` runs). No external packages.
