using System.Diagnostics;

// A small, honest benchmark for Dictionary.GetAlternateLookup<ReadOnlySpan<char>>.
// Scenario: a request arrives as one big space-separated blob of tokens. For each
// token I look up a weight in a Dictionary<string, long> and sum the hits. The
// classic way slices out a substring per token to do the lookup. The alternate
// lookup slices a ReadOnlySpan<char> instead and never allocates the key.

// ---- build the lookup table: 5,000 known keys -> a weight -------------------
var rng = new Random(42);
var table = new Dictionary<string, long>(StringComparer.Ordinal);
var vocab = new string[5_000];
for (int i = 0; i < vocab.Length; i++)
{
    var key = "k" + i.ToString("D5");
    vocab[i] = key;
    table[key] = rng.Next(1, 100);
}

// ---- build the input: ~200,000 tokens, ~70% of them known keys --------------
var sb = new System.Text.StringBuilder();
int tokenCount = 200_000;
for (int i = 0; i < tokenCount; i++)
{
    if (rng.NextDouble() < 0.70)
        sb.Append(vocab[rng.Next(vocab.Length)]);   // a hit
    else
        sb.Append("miss" + rng.Next(100000));        // a miss
    if (i != tokenCount - 1) sb.Append(' ');
}
string input = sb.ToString();
Console.WriteLine($"input: {input.Length:N0} chars, {tokenCount:N0} tokens, 5,000-key table");

// ---- version A: substring per token, then normal dictionary lookup ----------
static (long sum, long bytes, double ms) RunSubstring(string input, Dictionary<string, long> table)
{
    long before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    long sum = 0;
    int start = 0;
    for (int i = 0; i <= input.Length; i++)
    {
        if (i == input.Length || input[i] == ' ')
        {
            string token = input.Substring(start, i - start);   // allocates
            if (table.TryGetValue(token, out long w)) sum += w;
            start = i + 1;
        }
    }
    sw.Stop();
    long after = GC.GetAllocatedBytesForCurrentThread();
    return (sum, after - before, sw.Elapsed.TotalMilliseconds);
}

// ---- version B: span slice per token, alternate lookup, zero key allocs -----
static (long sum, long bytes, double ms) RunSpan(string input, Dictionary<string, long> table)
{
    var lookup = table.GetAlternateLookup<ReadOnlySpan<char>>();
    long before = GC.GetAllocatedBytesForCurrentThread();
    var sw = Stopwatch.StartNew();
    long sum = 0;
    ReadOnlySpan<char> span = input;
    int start = 0;
    for (int i = 0; i <= span.Length; i++)
    {
        if (i == span.Length || span[i] == ' ')
        {
            ReadOnlySpan<char> token = span.Slice(start, i - start);   // no alloc
            if (lookup.TryGetValue(token, out long w)) sum += w;
            start = i + 1;
        }
    }
    sw.Stop();
    long after = GC.GetAllocatedBytesForCurrentThread();
    return (sum, after - before, sw.Elapsed.TotalMilliseconds);
}

static double Median(Func<(long, long, double)> f, int rounds, out long sum, out long bytes)
{
    var times = new double[rounds];
    sum = 0; bytes = 0;
    for (int r = 0; r < rounds; r++)
    {
        var (s, b, ms) = f();
        times[r] = ms; sum = s; bytes = b;
    }
    Array.Sort(times);
    return times[rounds / 2];
}

// warmup
RunSubstring(input, table);
RunSpan(input, table);

const int rounds = 9;
double aMs = Median(() => RunSubstring(input, table), rounds, out long aSum, out long aBytes);
double bMs = Median(() => RunSpan(input, table), rounds, out long bSum, out long bBytes);

Console.WriteLine();
Console.WriteLine($"[A: Substring + lookup]  sum={aSum,12:N0}  median={aMs,7:F1} ms  allocated={aBytes / 1024.0,9:N0} KB");
Console.WriteLine($"[B: span alternate lookup] sum={bSum,10:N0}  median={bMs,7:F1} ms  allocated={bBytes / 1024.0,9:N0} KB");
Console.WriteLine();
Console.WriteLine($"same result: {aSum == bSum}");
Console.WriteLine($"allocation ratio A/B: {(bBytes == 0 ? double.PositiveInfinity : (double)aBytes / bBytes):F1}x");
Console.WriteLine($"time ratio A/B: {aMs / bMs:F2}x");
