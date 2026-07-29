# FrozenDictionary lookup

Benchmarks the three ways to hold a build-once, read-forever lookup table — `Dictionary<TKey,TValue>`, `ImmutableDictionary<TKey,TValue>`, and `FrozenDictionary<TKey,TValue>` — over 1,000 realistic string keys.

What it measures:

- Lookup throughput when every key exists (10M `TryGetValue` calls, best of 5)
- Lookup throughput when no key exists (miss-heavy workloads)
- Construction cost of each container ("the freeze fee")
- Break-even point: how many lookups until `ToFrozenDictionary()` has paid for itself

## Run it

```bash
dotnet run -c Release
```

Requires .NET 10.

📖 Article: _link added after publish_
