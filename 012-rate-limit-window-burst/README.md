# The Fixed-Window Seam

Every fixed-window rate limit has a seam at the window boundary: burst just before it and just after it, and a "10 requests per 10 seconds" policy admits ~19 requests in about a second. The sliding-window limiter with identical numbers doesn't have the seam.

The demo starts a minimal API with two rate-limiting policies (both `PermitLimit = 10`, `Window = 10s`, `QueueLimit = 0`):

| Policy | Limiter | Densest 10s span actually admitted |
|---|---|---|
| `/fixed` | `AddFixedWindowLimiter` | **19 requests** (all inside ~1.3s) |
| `/sliding` | `AddSlidingWindowLimiter`, 5 segments | **10 requests** — the configured limit |

The app then attacks itself with `HttpClient`: burns the window to zero, polls to locate a replenish boundary, goes quiet, bursts 9 requests just before the next boundary and 10 more just after it, and reports the densest 10-second span the limiter really allowed.

## Run it

```bash
dotnet run -c Release
```

Takes ~45 seconds (it has to sit through two real 10-second windows per policy). Requires the .NET 10 SDK (the rate limiting middleware itself has been in the box since .NET 7 — retarget the `.csproj` if needed).

📖 Article: _link added after publish_
