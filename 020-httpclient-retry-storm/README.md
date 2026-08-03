# HttpClient retry storm vs. the standard resilience handler

Demonstrates what a hand-rolled "retry x4, no delay" loop does to a struggling
dependency, compared with `AddStandardResilienceHandler` from
**Microsoft.Extensions.Http.Resilience**:

- **Phase 1** — naive retry loop: 20 calls become 80 server hits inside ~30 ms,
  all during the outage, zero successes.
- **Phase 2** — same outage, same retry budget, but exponential backoff + jitter:
  the later attempts land *after* recovery and most calls succeed.
- **Phase 3** — longer outage with a tuned circuit breaker: first attempts trip
  the breaker, everything else fails fast (`BrokenCircuitException`) without
  touching the server, and a probe call after recovery closes the circuit again.

One self-contained ASP.NET Core app hosts the flaky endpoint (503 while an
"outage" window is active) and runs all three client phases against it.

## Run it

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK. Retry delays and breaker thresholds are shrunk from
the production defaults so the whole demo finishes in about 12 seconds — the
shapes are unchanged.

📖 Article: _link added after publish_
