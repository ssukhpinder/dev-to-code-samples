# Dictionary ref upsert: GetValueRefOrAddDefault

Counting word frequencies is the textbook `Dictionary` upsert: check if the key
exists, then bump its counter. Written the obvious way (`TryGetValue` then
indexer assignment) that hashes the same key twice per token.
`CollectionsMarshal.GetValueRefOrAddDefault` hands you a `ref` straight into the
bucket, so you hash once and mutate in place.

This sample counts 5,000,000 tokens over a 20,000-word Zipf-skewed vocabulary,
both ways, checks the histograms are identical, then times each (median of 11
runs, allocations via `GC.GetAllocatedBytesForCurrentThread`).

## Run it

```bash
dotnet run -c Release
```

Needs the .NET 10 SDK. Everything is in `Program.cs`.

📖 Article: _link added after publish_
