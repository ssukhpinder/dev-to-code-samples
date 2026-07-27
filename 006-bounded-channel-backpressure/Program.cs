// Unbounded vs bounded channels: what backpressure actually buys you.
// One producer bursts work items into a channel; one consumer processes them
// slower than they arrive. We measure queue depth, managed heap, and timings
// for three configurations of the same pipeline.

using System.Diagnostics;
using System.Threading.Channels;

const int TotalItems = 100_000;
const int PayloadBytes = 2_048;   // stand-in for a serialized job (webhook body, email, etc.)
const int Capacity = 1_000;       // bounded channel capacity
const int WorkPasses = 24;        // simulated per-item handler cost (checksum passes)

Console.WriteLine($"runtime={Environment.Version}  items={TotalItems:N0}  payload={PayloadBytes:N0} B  capacity={Capacity:N0}");
Console.WriteLine();

// Warmup run so tiered compilation doesn't skew scenario 1. Results discarded.
await Run("warmup", Channel.CreateUnbounded<WorkItem>(UnboundedOpts()), 5_000, quiet: true);

var results = new List<ScenarioResult>
{
    await Run("unbounded", Channel.CreateUnbounded<WorkItem>(UnboundedOpts()), TotalItems, timeline: true),

    await Run("bounded-wait", Channel.CreateBounded<WorkItem>(BoundedOpts(BoundedChannelFullMode.Wait)), TotalItems),
};

long dropped = 0;
results.Add(await Run("bounded-drop-oldest",
    Channel.CreateBounded<WorkItem>(BoundedOpts(BoundedChannelFullMode.DropOldest),
        _ => Interlocked.Increment(ref dropped)),
    TotalItems, getDropped: () => Interlocked.Read(ref dropped)));

Console.WriteLine();
Console.WriteLine($"{"scenario",-20}{"enqueue",10}{"total",10}{"processed",12}{"dropped",10}{"peak depth",13}{"peak heap",12}");
foreach (var r in results)
    Console.WriteLine(
        $"{r.Name,-20}{r.EnqueueSeconds,9:F2}s{r.TotalSeconds,9:F2}s{r.Processed,12:N0}{r.Dropped,10:N0}{r.PeakDepth,13:N0}{r.PeakHeapMb,9:N0} MB");

static async Task<ScenarioResult> Run(
    string name, Channel<WorkItem> channel, int totalItems,
    bool quiet = false, bool timeline = false, Func<long>? getDropped = null)
{
    GC.Collect();
    GC.WaitForPendingFinalizers();
    long baseline = GC.GetTotalMemory(forceFullCollection: true);

    var reader = channel.Reader;
    var writer = channel.Writer;

    long written = 0;
    long processed = 0;
    long peakDepth = 0;
    long peakHeap = baseline;
    var samples = new List<(double T, long Depth, long Heap)>();
    var clock = Stopwatch.StartNew();
    using var samplerCts = new CancellationTokenSource();

    // Samples queue depth + managed heap every ~15 ms while the pipeline runs.
    // Depth is computed as written - processed - dropped, because the
    // single-consumer unbounded channel doesn't support reader.Count.
    var sampler = Task.Run(async () =>
    {
        while (!samplerCts.IsCancellationRequested)
        {
            long depth = Interlocked.Read(ref written)
                         - Interlocked.Read(ref processed)
                         - (getDropped?.Invoke() ?? 0);
            long heap = GC.GetTotalMemory(false);
            if (depth > peakDepth) peakDepth = depth;
            if (heap > peakHeap) peakHeap = heap;
            samples.Add((clock.Elapsed.TotalSeconds, depth, heap));
            try { await Task.Delay(15, samplerCts.Token); }
            catch (OperationCanceledException) { break; }
        }
    });

    var consumer = Task.Run(async () =>
    {
        await foreach (var item in reader.ReadAllAsync())
        {
            Process(item);
            Interlocked.Increment(ref processed);
        }
    });

    // Producer: as fast as it can go. WriteAsync only actually waits when a
    // bounded channel is full and FullMode is Wait — that's the backpressure.
    var enqueueClock = Stopwatch.StartNew();
    for (var i = 0; i < totalItems; i++)
    {
        var payload = new byte[PayloadBytes];
        payload[0] = (byte)i;
        await writer.WriteAsync(new WorkItem(i, payload));
        Interlocked.Increment(ref written);
    }
    writer.Complete();
    enqueueClock.Stop();

    await consumer;
    clock.Stop();
    samplerCts.Cancel();
    await sampler;

    var result = new ScenarioResult(
        name,
        enqueueClock.Elapsed.TotalSeconds,
        clock.Elapsed.TotalSeconds,
        Interlocked.Read(ref processed),
        getDropped?.Invoke() ?? 0,
        peakDepth,
        peakHeap / 1024.0 / 1024.0);

    if (!quiet)
    {
        Console.WriteLine(
            $"[{result.Name}] enqueue {result.EnqueueSeconds:F2}s, total {result.TotalSeconds:F2}s, " +
            $"processed {result.Processed:N0}, dropped {result.Dropped:N0}, " +
            $"peak depth {result.PeakDepth:N0}, peak heap {result.PeakHeapMb:N0} MB");

        if (timeline && samples.Count > 0)
        {
            Console.WriteLine($"[{result.Name}] timeline:");
            var step = Math.Max(1, samples.Count / 6);
            for (var s = 0; s < samples.Count; s += step)
                Console.WriteLine($"  t={samples[s].T,5:F2}s  depth={samples[s].Depth,8:N0}  heap={samples[s].Heap / 1024 / 1024,5:N0} MB");
        }
        Console.WriteLine();
    }

    return result;
}

// Simulated handler cost: checksums the payload a few times. Stands in for
// the serialize/compress/send work a real consumer does per item.
static void Process(WorkItem item)
{
    long sum = 0;
    for (var pass = 0; pass < WorkPasses; pass++)
    {
        var span = item.Payload.AsSpan();
        for (var i = 0; i < span.Length; i++) sum += span[i];
    }
    if (sum == long.MinValue) Console.WriteLine("unreachable"); // keep the JIT honest
}

static UnboundedChannelOptions UnboundedOpts() =>
    new() { SingleReader = true, SingleWriter = true };

static BoundedChannelOptions BoundedOpts(BoundedChannelFullMode mode) =>
    new(Capacity) { SingleReader = true, SingleWriter = true, FullMode = mode };

internal sealed record WorkItem(int Id, byte[] Payload);

internal sealed record ScenarioResult(
    string Name, double EnqueueSeconds, double TotalSeconds,
    long Processed, long Dropped, long PeakDepth, double PeakHeapMb);
