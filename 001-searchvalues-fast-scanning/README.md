# When SearchValues&lt;T&gt; Actually Pays Off

Benchmark comparing `SearchValues<char>` against `string.IndexOfAny` and a manual character loop — dense matches, sparse matches, bigger needle sets, and the per-call `Create` trap.

📖 Article: [When SearchValues&lt;T&gt; Actually Pays Off](https://dev.to/ssukhpinder/when-searchvalues-actually-pays-off-310l)

## Run it

```bash
cd SearchValuesDemo
dotnet run -c Release
```

Requires .NET 10 SDK (targets `net10.0`; retarget to `net8.0+` if needed — `SearchValues<char>` works from .NET 8).

## What it measures

1. **Dense matches, 5-char set** — a ~32 MB fake log with a delimiter every ~60 chars
2. **Sparse matches** — same size, one delimiter every ~40 KB (where SearchValues wins ~1.7x)
3. **Bigger needle set** — 12 chars instead of 5
4. **The trap** — `SearchValues.Create` per call vs `static readonly` (~2.8x slower)
