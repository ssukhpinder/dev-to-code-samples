# GeneratedRegex cold start

Measures what the three ways of holding a constant `Regex` cost you at process start, and what they pay back once warm, on .NET 10:

- **Cold start** (fresh process each time): construction + first match for interpreted, `RegexOptions.Compiled`, and `[GeneratedRegex]`
- **The hidden split**: how much of `Compiled`'s cold cost is one-time reflection-emit infrastructure vs per-pattern (spoiler: pattern #1 pays ~19 ms, pattern #2 pays ~2 ms)
- **Warm throughput**: parsing a 250,000-line synthetic log corpus with named group extraction, best of 5 passes

## Run it

```bash
dotnet run -c Release                        # warm throughput, best of 5
dotnet run -c Release -- cold generated      # cold cost in a fresh process (also: interpreted | compiled)
dotnet run -c Release -- cold-pair           # first vs second Compiled pattern in one process
```

Requires .NET 10.

📖 Article: _link added after publish_
