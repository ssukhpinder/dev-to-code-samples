# 7.8 MB of Keys I Allocated Just to Throw Away

Demonstrates `Dictionary<TKey, TValue>.GetAlternateLookup<ReadOnlySpan<char>>()` — looking
up string-keyed dictionary entries with a `ReadOnlySpan<char>` slice instead of allocating a
substring per lookup. Tokenizes a ~1.5 MB blob of 200,000 space-separated tokens, sums weights
from a 5,000-key table two ways, and compares time and allocations.

## What it shows

- **A — `Substring` + `TryGetValue`**: allocates one string per token before the lookup.
- **B — `GetAlternateLookup<ReadOnlySpan<char>>`**: slices a span, looks up directly, allocates nothing for keys.

Both produce the identical sum. On my run (.NET 10, Release, small Linux container, median of 9):
version A allocated ~7,812 KB and ran ~23 ms; version B allocated 0 KB for keys and ran ~15 ms.

## How to run

```bash
dotnet run -c Release
```

Requires the .NET 10 SDK. `GetAlternateLookup` needs a comparer that implements
`IAlternateEqualityComparer<ReadOnlySpan<char>, string>` — the built-in ordinal and
ordinal-ignore-case string comparers qualify.

📖 Article: _link added after publish_
