# IExceptionHandler Chain: Deliberate Error Responses for Minimal APIs

Demonstrates what an ASP.NET Core API answers when an endpoint throws — the leaky Development default, the mute Production default — and how an `IExceptionHandler` chain plus `AddProblemDetails()` turns domain exceptions into deliberate `application/problem+json` responses (404/409) with a vague-on-purpose 500 fallback that logs the real exception with a matching `traceId`. Includes a `--swapped` mode that registers the fallback handler first, proving that registration order is the chain order.

What's inside:

- `Program.cs` — minimal API whose endpoints throw freely, plus a self-probe that prints each raw HTTP exchange
- `Domain.cs` — `ProductNotFoundException`, `StaleInventoryException`
- `Handlers.cs` — `DomainExceptionHandler` (maps known exceptions, returns `false` for the rest) and `UnhandledExceptionHandler` (logs, answers a generic 500)

## Run it

```bash
dotnet run                        # handler chain on (Production): 404 / 409 / generic 500 as problem+json
dotnet run -- --baseline          # no handling (Production): 500, zero-byte body
dotnet run -- --baseline --dev    # no handling (Development): 500 with a full stack trace as the body
dotnet run -- --swapped           # fallback registered first: the 404 and 409 vanish, everything is a 500
```

Each run starts the API on `127.0.0.1:5090`, probes four endpoints, prints the exchanges, and exits.

📖 Article: _link added after publish_
