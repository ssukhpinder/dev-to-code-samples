// FrozenDictionary vs Dictionary vs ImmutableDictionary
// Build-once, read-forever lookup tables: what does the freeze cost, and what does it buy?
//
// Run: dotnet run -c Release

using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Diagnostics;

const int KeyCount = 1_000;
const int QueryMask = 4095;              // query arrays are 4096 long, cycled
const int LookupOps = 10_000_000;
const int Rounds = 5;                    // best-of-5 per measurement
const int ConstructionReps = 2_000;

Console.WriteLine($"keys={KeyCount:N0}, lookups per round={LookupOps:N0}, best of {Rounds}");
Console.WriteLine($".NET {Environment.Version}, {(Environment.Is64BitProcess ? "x64" : "x86")}, server GC={System.Runtime.GCSettings.IsServerGC}");
Console.WriteLine();

// ---------------------------------------------------------------------------
// Build realistic feature-flag style keys: "flags:checkout:variant-0042"
// ---------------------------------------------------------------------------
string[] areas = ["checkout", "search", "auth", "billing", "catalog", "shipping", "profile", "reports"];
string[] names = ["variant", "rollout", "dark-mode", "new-badge", "fast-path", "beta", "kill-switch", "banner"];

var pairs = new KeyValuePair<string, string>[KeyCount];
for (var i = 0; i < KeyCount; i++)
{
    var key = $"flags:{areas[i % areas.Length]}:{names[(i / 8) % names.Length]}-{i:D4}";
    pairs[i] = new(key, $"enabled={(i % 3 != 0).ToString().ToLowerInvariant()};ring={i % 5}");
}

// The three contenders, all built from the same pairs, all default (ordinal) comparer.
var dictionary = new Dictionary<string, string>(pairs);
var immutable  = pairs.ToImmutableDictionary();
var frozen     = pairs.ToFrozenDictionary();

// Pre-shuffled query streams: one of keys that exist, one of keys that don't.
var rng = new Random(42);
var hitQueries  = new string[QueryMask + 1];
var missQueries = new string[QueryMask + 1];
for (var i = 0; i < hitQueries.Length; i++)
{
    hitQueries[i]  = pairs[rng.Next(KeyCount)].Key;
    missQueries[i] = $"flags:{areas[rng.Next(areas.Length)]}:retired-{rng.Next(KeyCount):D4}";
}

// ---------------------------------------------------------------------------
// Lookup throughput (each loop uses the concrete type, like real code does)
// ---------------------------------------------------------------------------
Console.WriteLine("== lookups, all keys present ==");
var dictHit = Bench("Dictionary          ", () =>
{
    long checksum = 0;
    for (var i = 0; i < LookupOps; i++)
        if (dictionary.TryGetValue(hitQueries[i & QueryMask], out var v))
            checksum += v.Length;
    return checksum;
});
var immuHit = Bench("ImmutableDictionary ", () =>
{
    long checksum = 0;
    for (var i = 0; i < LookupOps; i++)
        if (immutable.TryGetValue(hitQueries[i & QueryMask], out var v))
            checksum += v.Length;
    return checksum;
});
var frozHit = Bench("FrozenDictionary    ", () =>
{
    long checksum = 0;
    for (var i = 0; i < LookupOps; i++)
        if (frozen.TryGetValue(hitQueries[i & QueryMask], out var v))
            checksum += v.Length;
    return checksum;
});

Console.WriteLine();
Console.WriteLine("== lookups, no key present (miss-heavy) ==");
var dictMiss = Bench("Dictionary          ", () =>
{
    long found = 0;
    for (var i = 0; i < LookupOps; i++)
        if (dictionary.TryGetValue(missQueries[i & QueryMask], out _))
            found++;
    return found;
});
var immuMiss = Bench("ImmutableDictionary ", () =>
{
    long found = 0;
    for (var i = 0; i < LookupOps; i++)
        if (immutable.TryGetValue(missQueries[i & QueryMask], out _))
            found++;
    return found;
});
var frozMiss = Bench("FrozenDictionary    ", () =>
{
    long found = 0;
    for (var i = 0; i < LookupOps; i++)
        if (frozen.TryGetValue(missQueries[i & QueryMask], out _))
            found++;
    return found;
});

// ---------------------------------------------------------------------------
// Construction cost: what the freeze charges at the door
// ---------------------------------------------------------------------------
Console.WriteLine();
Console.WriteLine($"== construction, {KeyCount:N0} entries, avg of {ConstructionReps:N0} builds ==");
var dictBuild = BenchBuild("new Dictionary(pairs)  ", () => new Dictionary<string, string>(pairs).Count);
var immuBuild = BenchBuild("ToImmutableDictionary()", () => pairs.ToImmutableDictionary().Count);
var frozBuild = BenchBuild("ToFrozenDictionary()   ", () => pairs.ToFrozenDictionary().Count);

// ---------------------------------------------------------------------------
// Break-even: how many lookups until the freeze has paid for itself?
// ---------------------------------------------------------------------------
Console.WriteLine();
var extraBuildNs = (frozBuild - dictBuild) * 1_000.0;         // µs -> ns
var savedPerHitNs = dictHit - frozHit;                        // ns per lookup
var savedPerMissNs = dictMiss - frozMiss;
Console.WriteLine($"freeze surcharge over Dictionary : {extraBuildNs / 1000.0:F0} µs per build");
if (savedPerHitNs > 0)
    Console.WriteLine($"break-even vs Dictionary (hits)  : ~{extraBuildNs / savedPerHitNs:N0} lookups");
if (savedPerMissNs > 0)
    Console.WriteLine($"break-even vs Dictionary (misses): ~{extraBuildNs / savedPerMissNs:N0} lookups");

// keep immutable-vs-frozen ratios handy for the article
Console.WriteLine($"ImmutableDictionary vs frozen    : {immuHit / frozHit:F1}x slower on hits, {immuMiss / frozMiss:F1}x on misses");
Console.WriteLine($"Immutable build vs frozen build  : frozen costs {frozBuild / immuBuild:F1}x an immutable build");

// ---------------------------------------------------------------------------
// Harness
// ---------------------------------------------------------------------------
static double Bench(string label, Func<long> run)
{
    run();                                   // warmup / JIT
    var best = double.MaxValue;
    long checksum = 0;
    for (var r = 0; r < Rounds; r++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var sw = Stopwatch.StartNew();
        checksum = run();
        sw.Stop();
        best = Math.Min(best, sw.Elapsed.TotalNanoseconds / LookupOps);
    }
    Console.WriteLine($"{label}: {best,6:F1} ns/lookup   (checksum {checksum})");
    return best;
}

static double BenchBuild(string label, Func<int> build)
{
    build();                                 // warmup / JIT
    var best = double.MaxValue;
    for (var r = 0; r < Rounds; r++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        var sw = Stopwatch.StartNew();
        var sink = 0;
        for (var i = 0; i < ConstructionReps; i++)
            sink += build();
        sw.Stop();
        if (sink == 0) throw new InvalidOperationException();
        best = Math.Min(best, sw.Elapsed.TotalMicroseconds / ConstructionReps);
    }
    Console.WriteLine($"{label}: {best,8:F1} µs/build");
    return best;
}
