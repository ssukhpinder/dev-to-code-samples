using System.Diagnostics;
using System.Text.RegularExpressions;

// Two modes:
//   dotnet run -c Release -- cold <interpreted|compiled|generated>
//       measures construction + first match in a FRESH process (run each engine separately)
//   dotnet run -c Release
//       measures warm throughput over a 250k-line synthetic log corpus, best of 5 passes

const string Sample = "2026-08-02T07:14:33 [WARN] Order 8123 retried after transient failure";

if (args is ["cold-pair"])
{
    // How much of the Compiled cold cost is one-time infrastructure vs per-pattern?
    // Construct + first-match TWO different Compiled patterns in the same fresh process.
    var sw2 = Stopwatch.StartNew();
    var first = new Regex(Patterns.LogLine, RegexOptions.Compiled);
    first.IsMatch(Sample);
    sw2.Stop();
    Console.WriteLine($"compiled pattern #1 (log line): {sw2.Elapsed.TotalMilliseconds,7:F2} ms construct + first match");

    sw2.Restart();
    var second = new Regex(Patterns.Guid, RegexOptions.Compiled);
    second.IsMatch("a3f1c9d2-8b4e-4f6a-9c0d-2e7b5a1f3c8e");
    sw2.Stop();
    Console.WriteLine($"compiled pattern #2 (guid):     {sw2.Elapsed.TotalMilliseconds,7:F2} ms construct + first match");
    return;
}

if (args is ["cold", var engine])
{
    var sw = Stopwatch.StartNew();
    Regex re = engine switch
    {
        "interpreted" => new Regex(Patterns.LogLine),
        "compiled"    => new Regex(Patterns.LogLine, RegexOptions.Compiled),
        "generated"   => LogLine.Parser(),
        _             => throw new ArgumentException($"unknown engine '{engine}'")
    };
    sw.Stop();
    double ctorMs = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    bool hit = re.IsMatch(Sample);
    sw.Stop();
    double firstMs = sw.Elapsed.TotalMilliseconds;

    Console.WriteLine($"{engine,-11} construct: {ctorMs,7:F2} ms   first match: {firstMs,7:F2} ms   total: {ctorMs + firstMs,7:F2} ms   (matched: {hit})");
    return;
}

// ---------- warm throughput mode ----------

string[] lines = BuildCorpus(250_000);

(string Name, Regex Re)[] engines =
[
    ("interpreted", new Regex(Patterns.LogLine)),
    ("compiled",    new Regex(Patterns.LogLine, RegexOptions.Compiled)),
    ("generated",   LogLine.Parser()),
];

// Warm every engine before timing anything, so JIT cost stays out of the passes.
foreach (var (_, re) in engines)
    for (int i = 0; i < 20_000; i++)
        re.Match(lines[i]);

Console.WriteLine($"corpus: {lines.Length:N0} lines, 5 timed passes per engine (after warmup)");
Console.WriteLine();

foreach (var (name, re) in engines)
{
    var times = new double[5];
    int hits = 0;

    for (int pass = 0; pass < times.Length; pass++)
    {
        hits = 0;
        long levelChars = 0;
        var sw = Stopwatch.StartNew();
        foreach (var line in lines)
        {
            var m = re.Match(line);
            if (m.Success)
            {
                hits++;
                levelChars += m.Groups["level"].ValueSpan.Length;
            }
        }
        sw.Stop();
        SinkHolder.Sink = levelChars; // keep the group reads observable
        times[pass] = sw.Elapsed.TotalMilliseconds;
    }

    Array.Sort(times);
    Console.WriteLine($"{name,-11} best: {times[0],7:F1} ms   median: {times[2],7:F1} ms   matched {hits:N0}/{lines.Length:N0}");
}

static string[] BuildCorpus(int count)
{
    string[] levels = ["TRACE", "DEBUG", "INFO", "WARN", "ERROR"];
    string[] noise =
    [
        "connection reset by peer",
        "  at Retry.ExecuteAsync(Func`1 operation)",
        "---- heartbeat ----",
        "warn: no structured prefix on this one",
    ];

    var rng = new Random(42); // deterministic corpus, same lines every run
    var result = new string[count];
    var baseTime = new DateTime(2026, 8, 1, 0, 0, 0, DateTimeKind.Utc);

    for (int i = 0; i < count; i++)
    {
        if (rng.Next(100) < 15)
        {
            result[i] = noise[rng.Next(noise.Length)];
            continue;
        }

        var ts = baseTime.AddSeconds(rng.Next(0, 172_800));
        string level = levels[rng.Next(levels.Length)];
        string verb = rng.Next(2) == 0 ? "processed" : "retried after transient failure";
        result[i] = $"{ts:yyyy-MM-dd'T'HH:mm:ss} [{level}] Order {rng.Next(1000, 9999)} {verb}";
    }

    return result;
}

internal static class Patterns
{
    public const string LogLine =
        @"^(?<ts>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}) \[(?<level>[A-Z]+)\] (?<msg>.+)$";

    public const string Guid =
        @"^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$";
}

internal static partial class LogLine
{
    [GeneratedRegex(Patterns.LogLine)]
    public static partial Regex Parser();
}

internal static class SinkHolder
{
    public static long Sink;
}
