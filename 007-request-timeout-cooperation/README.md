# Request Timeouts Are a Contract, Not a Kill Switch

ASP.NET Core's request timeouts middleware (`AddRequestTimeouts` / `UseRequestTimeouts`) only cancels a `CancellationToken` — it can't stop a handler that never looks at it.

The demo starts a minimal API with a 2-second default timeout policy and hits the same 6-second "slow dependency" four ways:

| Endpoint | What it does | Result |
|---|---|---|
| `/honors-token` | passes the request token into the work | **504 after ~2s** |
| `/tight` | same, with a 500 ms `WithRequestTimeout` policy | **504 after ~0.5s** |
| `/ignores-token` | same work, token never consulted | **200 after ~6s** — timeout fired, nobody listened |
| `/opted-out` | `DisableRequestTimeout()` for legit long work | 200 after ~6s, on purpose |

## Run it

```bash
dotnet run -c Release
```

The app self-probes its own endpoints with `HttpClient` and prints status codes plus wall-clock timings.

Requires .NET 10 SDK (the middleware itself exists since .NET 8 — retarget the `.csproj` if needed).

📖 Article: _link added after publish_
