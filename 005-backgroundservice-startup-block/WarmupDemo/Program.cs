using System.Collections.Concurrent;
using System.Diagnostics;

// One binary, four modes. WARMUP_MODE = blocking | yield | concurrent | startasync
// Measures: process start -> Kestrel listening -> first HTTP response -> cache warm.
// Multi-targets net8.0 and net10.0 so you can watch the behavior change between them.

Clock.Start();
var mode = Environment.GetEnvironmentVariable("WARMUP_MODE") ?? "blocking";
Clock.Log($"process started (mode={mode}, {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription})");

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();

builder.Services.AddSingleton<PriceCache>();
builder.Services.AddSingleton(new WarmupOptions(mode));

if (mode == "startasync")
{
    // Plain IHostedService doing the same work synchronously inside StartAsync.
    builder.Services.AddHostedService<BlockingStartupService>();
}
else
{
    builder.Services.AddHostedService<CacheWarmupService>();
}

if (mode == "concurrent")
{
    // .NET 8+: start all hosted services at the same time instead of one by one.
    builder.Services.Configure<HostOptions>(o => o.ServicesStartConcurrently = true);
}

var app = builder.Build();

app.MapGet("/prices/{sku}", (string sku, PriceCache cache) =>
    cache.TryGet(sku, out var price)
        ? Results.Ok(new { sku, price, warm = cache.IsWarm })
        : Results.NotFound(new { sku, warm = cache.IsWarm }));

app.Lifetime.ApplicationStarted.Register(() => Clock.Log("ApplicationStarted — Kestrel is listening"));

var serverTask = app.RunAsync("http://127.0.0.1:5199");

// Self-probe from the same process: hammer the endpoint until it answers.
using var client = new HttpClient { Timeout = TimeSpan.FromMilliseconds(500) };
string body;
while (true)
{
    try
    {
        body = await client.GetStringAsync("http://127.0.0.1:5199/prices/sku-0-0");
        break;
    }
    catch
    {
        await Task.Delay(20);
    }
}
Clock.Log($"first HTTP response: {body}");

// Keep polling until the cache reports warm, so we can see the full timeline.
var cache = app.Services.GetRequiredService<PriceCache>();
while (!cache.IsWarm)
{
    await Task.Delay(50);
}
Clock.Log("cache reports warm");

await app.StopAsync();
await serverTask;

record WarmupOptions(string Mode);

static class Clock
{
    private static readonly Stopwatch Sw = new();
    public static void Start() => Sw.Start();
    public static void Log(string message) =>
        Console.WriteLine($"[{Sw.ElapsedMilliseconds,5} ms] {message}");
}

sealed class PriceCache
{
    private readonly ConcurrentDictionary<string, decimal> _prices = new();
    private int _segmentsLoaded;

    public const int TotalSegments = 40;

    public bool IsWarm => Volatile.Read(ref _segmentsLoaded) == TotalSegments;

    public bool TryGet(string sku, out decimal price) => _prices.TryGetValue(sku, out price);

    public void LoadSegment(int segment)
    {
        for (var i = 0; i < 25; i++)
        {
            _prices[$"sku-{segment}-{i}"] = 10m + segment + i * 0.25m;
        }
        Interlocked.Increment(ref _segmentsLoaded);
    }
}

sealed class BlockingStartupService(PriceCache cache) : IHostedService
{
    public Task StartAsync(CancellationToken cancellationToken)
    {
        Clock.Log($"StartAsync warmup started on thread {Environment.CurrentManagedThreadId}");

        for (var segment = 0; segment < PriceCache.TotalSegments; segment++)
        {
            Thread.Sleep(80);
            cache.LoadSegment(segment);
        }

        Clock.Log($"StartAsync warmup finished — {PriceCache.TotalSegments} segments loaded");
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}

sealed class CacheWarmupService(PriceCache cache, WarmupOptions options) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (options.Mode == "yield")
        {
            // The one-line fix: give the thread back to the host before doing anything heavy.
            await Task.Yield();
        }

        Clock.Log($"warmup started on thread {Environment.CurrentManagedThreadId}");

        for (var segment = 0; segment < PriceCache.TotalSegments; segment++)
        {
            // Stand-in for synchronous I/O: a legacy pricing file, a blocking DB driver,
            // a vendor SDK without async APIs. The point is: no await in sight.
            Thread.Sleep(80);
            cache.LoadSegment(segment);
        }

        Clock.Log($"warmup finished — {PriceCache.TotalSegments} segments loaded");
    }
}
