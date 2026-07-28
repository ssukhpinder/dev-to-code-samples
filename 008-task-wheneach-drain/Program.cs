using System.Diagnostics;

// Demo for: Task.WhenEach vs the classic Task.WhenAny drain loop.
// Part 1: time-to-first-result for a small fan-out.
// Part 2: cost of draining a large batch with WhenAny vs WhenEach vs WhenAll.
// Part 3: how early a failure becomes visible.

var sw = Stopwatch.StartNew();

// ---------------------------------------------------------------
Console.WriteLine("== Part 1: five fan-out calls, who waits for whom ==");

sw.Restart();
var all = await Task.WhenAll(FanOut());
Console.WriteLine($"WhenAll : all {all.Length} results usable at {sw.ElapsedMilliseconds} ms");

sw.Restart();
await foreach (var t in Task.WhenEach(FanOut()))
    Console.WriteLine($"WhenEach: {await t,-14} usable at {sw.ElapsedMilliseconds,5} ms");

// ---------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== Part 2: draining 5,000 tasks (delays 1-50 ms) ==");

await MeasureAsync("WhenAll (baseline)", () => DrainWhenAll(SpawnBatch(5_000)));
await MeasureAsync("WhenEach", () => DrainWhenEach(SpawnBatch(5_000)));
await MeasureAsync("WhenAny drain loop", () => DrainWhenAny(SpawnBatch(5_000)));

// ---------------------------------------------------------------
Console.WriteLine();
Console.WriteLine("== Part 3: one of four calls fails at 400 ms ==");

sw.Restart();
try
{
    await Task.WhenAll(MixedFanOut());
}
catch (Exception ex)
{
    Console.WriteLine($"WhenAll : learned \"{ex.Message}\" at {sw.ElapsedMilliseconds} ms; the three good results are gone");
}

sw.Restart();
await foreach (var t in Task.WhenEach(MixedFanOut()))
{
    if (t.IsCompletedSuccessfully)
        Console.WriteLine($"WhenEach: {t.Result,-9} ok at {sw.ElapsedMilliseconds,4} ms");
    else
        Console.WriteLine($"WhenEach: \"{t.Exception!.InnerException!.Message}\" seen at {sw.ElapsedMilliseconds,4} ms; other calls unaffected");
}

// ---------------------------------------------------------------

static Task<string>[] FanOut() =>
[
    Call("profile", 150),
    Call("orders", 300),
    Call("recs", 500),
    Call("inventory", 800),
    Call("legacy-pricing", 1_200),
];

static Task<string>[] MixedFanOut() =>
[
    Call("profile", 150),
    Call("orders", 300),
    Fail("recs", 400),
    Call("inventory", 800),
];

static async Task<string> Call(string name, int ms)
{
    await Task.Delay(ms);
    return name;
}

static async Task<string> Fail(string name, int ms)
{
    await Task.Delay(ms);
    throw new InvalidOperationException($"{name} exploded");
}

// One batch of n tasks finishing in a ragged burst. Fixed seed so every
// strategy drains an identical delay distribution.
static Task<int>[] SpawnBatch(int n)
{
    var rng = new Random(42);
    var tasks = new Task<int>[n];
    for (var i = 0; i < n; i++)
        tasks[i] = Work(i, rng.Next(1, 50));
    return tasks;

    static async Task<int> Work(int id, int delayMs)
    {
        await Task.Delay(delayMs);
        return id;
    }
}

// The classic pre-.NET 9 pattern: WhenAny + Remove until the list is empty.
static async Task<long> DrainWhenAny(Task<int>[] tasks)
{
    long sum = 0;
    var pending = new List<Task<int>>(tasks);
    while (pending.Count > 0)
    {
        var done = await Task.WhenAny(pending);
        pending.Remove(done);
        sum += await done;
    }
    return sum;
}

static async Task<long> DrainWhenEach(Task<int>[] tasks)
{
    long sum = 0;
    await foreach (var t in Task.WhenEach(tasks))
        sum += await t;
    return sum;
}

static async Task<long> DrainWhenAll(Task<int>[] tasks)
{
    long sum = 0;
    foreach (var r in await Task.WhenAll(tasks))
        sum += r;
    return sum;
}

static async Task MeasureAsync(string label, Func<Task<long>> run)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();

    var proc = Process.GetCurrentProcess();
    var alloc0 = GC.GetTotalAllocatedBytes(precise: true);
    var cpu0 = proc.TotalProcessorTime;
    var sw = Stopwatch.StartNew();

    var sum = await run();

    sw.Stop();
    proc.Refresh();
    var cpuMs = (proc.TotalProcessorTime - cpu0).TotalMilliseconds;
    var allocMb = (GC.GetTotalAllocatedBytes(precise: true) - alloc0) / (1024.0 * 1024.0);

    Console.WriteLine($"{label,-20} elapsed {sw.ElapsedMilliseconds,5} ms | cpu {cpuMs,7:F0} ms | allocated {allocMb,8:F1} MB | checksum {sum}");
}
