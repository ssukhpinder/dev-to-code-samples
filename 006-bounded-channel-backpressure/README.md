# Bounded Channel Backpressure

What an unbounded `System.Threading.Channels` queue actually costs when the producer outruns the consumer — and what `Channel.CreateBounded` buys you.

One producer bursts 100,000 work items (2 KB payload each) into a channel while one consumer processes them slower than they arrive. The demo runs the same pipeline three ways and measures queue depth, managed heap, and timings:

1. **unbounded** — queue depth peaks around 90k items, heap peaks near 190 MB
2. **bounded, `FullMode.Wait`** — producer is slowed to consumer pace (backpressure), depth capped at 1,000, heap stays under 20 MB, same total finish time
3. **bounded, `FullMode.DropOldest`** — load shedding: finishes almost instantly but throws away ~97% of the work

## Run it

```bash
dotnet run -c Release
```

Requires .NET 10 SDK (works on .NET 8/9 too if you retarget the `.csproj`).

📖 Article: _link added after publish_
