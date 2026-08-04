using System.Diagnostics;
using System.Runtime;
using System.Runtime.InteropServices;

// A word-frequency count. The kind of thing you write without thinking:
// read a token, bump its counter, repeat. The bump is where the money goes.

const int TokenCount = 5_000_000;
const int VocabSize = 20_000;

string[] tokens = BuildTokens(TokenCount, VocabSize, seed: 1234);
Console.WriteLine($"tokens: {tokens.Length:N0}, distinct vocab: {VocabSize:N0}");
Console.WriteLine($"GC: {(GCSettings.IsServerGC ? "server" : "workstation")}, container");
Console.WriteLine();

// Correctness: both approaches must produce the exact same histogram.
var a = CountTryGetValue(tokens);
var b = CountRef(tokens);
Console.WriteLine($"identical results: {SameCounts(a, b)}  (distinct keys: {a.Count:N0})");
Console.WriteLine();

// Warm up the JIT so we're timing the algorithm, not first-call compilation.
for (int i = 0; i < 3; i++) { CountTryGetValue(tokens); CountRef(tokens); }

Measure("TryGetValue + indexer (two lookups)", () => CountTryGetValue(tokens));
Measure("GetValueRefOrAddDefault (one lookup)", () => CountRef(tokens));

// --- the two counters ---

static Dictionary<string, int> CountTryGetValue(string[] tokens)
{
    var counts = new Dictionary<string, int>();
    foreach (var t in tokens)
    {
        if (counts.TryGetValue(t, out int c))
            counts[t] = c + 1;   // second lookup, same key, same hash
        else
            counts[t] = 1;       // second lookup on the miss path too
    }
    return counts;
}

static Dictionary<string, int> CountRef(string[] tokens)
{
    var counts = new Dictionary<string, int>();
    foreach (var t in tokens)
    {
        ref int slot = ref CollectionsMarshal.GetValueRefOrAddDefault(counts, t, out _);
        slot++;              // slot points straight into the bucket
    }
    return counts;
}

// --- helpers ---

static void Measure(string label, Func<Dictionary<string, int>> run)
{
    const int Runs = 11;
    var times = new List<double>(Runs);
    long allocBefore = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < Runs; i++)
    {
        var sw = Stopwatch.StartNew();
        var d = run();
        sw.Stop();
        GC.KeepAlive(d);
        times.Add(sw.Elapsed.TotalMilliseconds);
    }
    long allocAfter = GC.GetAllocatedBytesForCurrentThread();
    times.Sort();
    double median = times[times.Count / 2];
    double allocPerRunKb = (allocAfter - allocBefore) / (double)Runs / 1024.0;
    Console.WriteLine($"{label,-42} median {median,7:F1} ms   ~{allocPerRunKb,8:N0} KB/run");
}

static bool SameCounts(Dictionary<string, int> x, Dictionary<string, int> y)
{
    if (x.Count != y.Count) return false;
    foreach (var kv in x)
        if (!y.TryGetValue(kv.Key, out var v) || v != kv.Value) return false;
    return true;
}

static string[] BuildTokens(int count, int vocab, int seed)
{
    // Pre-intern a vocabulary, then draw with a skew so most tokens are repeats
    // (a real corpus is Zipf-ish: a few words dominate, a long tail is rare).
    var words = new string[vocab];
    for (int i = 0; i < vocab; i++) words[i] = "word_" + i.ToString("D5");

    var rng = new Random(seed);
    var tokens = new string[count];
    for (int i = 0; i < count; i++)
    {
        // square the uniform draw -> bias toward low indices (frequent words)
        double u = rng.NextDouble();
        int idx = (int)(u * u * vocab);
        if (idx >= vocab) idx = vocab - 1;
        tokens[i] = words[idx];
    }
    return tokens;
}
