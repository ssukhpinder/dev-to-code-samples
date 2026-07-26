# Does `IAsyncEnumerable` Actually Stream? (plus native SSE in .NET 10)

Self-contained .NET 10 demo that measures when bytes actually leave a minimal API. One fake long-running job yields a step every ~400 ms, exposed three ways:

1. Plain `IAsyncEnumerable<T>` returned from `MapGet` (JSON array), with tiny and ~4 KB items
2. The same producer wrapped in .NET 10's `TypedResults.ServerSentEvents` (`text/event-stream`, per-event type + id)
3. The plain JSON endpoint consumed progressively by a .NET client via `JsonSerializer.DeserializeAsyncEnumerable`

The app starts Kestrel on localhost, probes itself with a raw `HttpClient` stream reader, and logs the elapsed time and size of every read — so you can see exactly when each element hit the wire.

📖 Article: _link added after publish_

## Run it

```bash
cd StreamingDemo
dotnet run -c Release
```

Requires the .NET 10 SDK (targets `net10.0`, no external packages).

## What it shows

1. **The buffering folklore didn't survive the stopwatch** — the JSON array streams per item on .NET 10: eight 40 B reads, one every ~400 ms, not one 321 B read at the end.
2. **Bigger items too** — ~4 KB elements flush on the same per-item rhythm.
3. **SSE framing costs a little, buys a lot** — 520 B vs 321 B for eight tiny events, in exchange for `event:`/`id:` framing, `EventSource` support in every browser, and reconnect semantics.
4. **Headers wait for the first byte** — in both variants the response headers left the server together with the first item, not before.
5. **Service-to-service needs no SSE** — `DeserializeAsyncEnumerable` consumes the half-open JSON array item by item just fine.
