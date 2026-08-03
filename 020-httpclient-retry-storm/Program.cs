// Retry storm demo: what a hand-rolled retry loop does to a struggling
// dependency, versus the standard resilience handler's backoff + circuit breaker.
//
// One process hosts a flaky endpoint (503 while an "outage" is active) and
// then hits it three ways:
//   phase 1 — hand-rolled retry x4, no delay          (the storm)
//   phase 2 — standard handler, exponential backoff   (retries that land)
//   phase 3 — standard handler, circuit breaker tuned (fail fast, stop hammering)
//
// Delays are shrunk from the production defaults so the whole demo runs in
// seconds. The shapes are unchanged.

using System.Collections.Concurrent;
using System.Diagnostics;
using Polly.CircuitBreaker;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Logging.AddFilter("Polly", LogLevel.None);
builder.WebHost.UseUrls("http://127.0.0.1:5199");

var state = new FlakyState();
builder.Services.AddSingleton(state);

// Phase 2 client: standard resilience handler, defaults except the retry base
// delay (production default is 2s — shrunk to 500ms so the demo fits in seconds).
builder.Services.AddHttpClient("backoff", c => c.BaseAddress = new Uri("http://127.0.0.1:5199"))
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.Delay = TimeSpan.FromMilliseconds(500);
    });

// Phase 3 client: same handler, circuit breaker tuned so 40 demo calls can
// actually trip it (the default MinimumThroughput of 100 is sized for real traffic).
builder.Services.AddHttpClient("breaker", c => c.BaseAddress = new Uri("http://127.0.0.1:5199"))
    .AddStandardResilienceHandler(o =>
    {
        o.Retry.Delay = TimeSpan.FromMilliseconds(200);
        o.AttemptTimeout.Timeout = TimeSpan.FromSeconds(2); // validation: sampling >= 2x attempt timeout
        o.CircuitBreaker.FailureRatio = 0.5;
        o.CircuitBreaker.MinimumThroughput = 20;
        o.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(10);
        o.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(2);
    });

var app = builder.Build();

app.MapGet("/inventory", (FlakyState s) =>
{
    var duringOutage = s.OutageActive;
    s.RecordHit(duringOutage);
    return duringOutage
        ? Results.StatusCode(StatusCodes.Status503ServiceUnavailable)
        : Results.Ok(new { sku = "WIDGET-7", qty = 42 });
});

await app.StartAsync();

var factory = app.Services.GetRequiredService<IHttpClientFactory>();

// ---------- phase 1: the loop I used to write ----------
using var naiveHttp = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5199") };
var backoffHttp = factory.CreateClient("backoff");
var breakerHttp = factory.CreateClient("breaker");

// Warm up connections outside any outage so hit timings measure retry
// behavior, not TCP handshakes.
await naiveHttp.GetAsync("/inventory");
await backoffHttp.GetAsync("/inventory");
await breakerHttp.GetAsync("/inventory");

await RunPhase(
    "phase 1: hand-rolled retry x4, no delay (outage 2s)",
    outageSeconds: 2.0, calls: 20,
    call: () => NaiveCall(naiveHttp));

// ---------- phase 2: same outage, retries with backoff + jitter ----------
await RunPhase(
    "phase 2: standard handler, exponential backoff (base 500ms, outage 2s)",
    outageSeconds: 2.0, calls: 20,
    call: () => PipelineCall(backoffHttp));

// ---------- phase 3: longer outage, circuit breaker armed ----------
var phase3Start = Stopwatch.GetTimestamp();
await RunPhase(
    "phase 3: standard handler + circuit breaker (longer outage: 6s)",
    outageSeconds: 6.0, calls: 40,
    call: () => PipelineCall(breakerHttp));

// Wait out the rest of the outage plus the break duration, then send one probe.
var elapsed = Stopwatch.GetElapsedTime(phase3Start);
var wait = TimeSpan.FromSeconds(6.5) - elapsed + TimeSpan.FromSeconds(2);
if (wait > TimeSpan.Zero) await Task.Delay(wait);

var probe = await PipelineCall(breakerHttp);
Console.WriteLine($"probe after recovery : {(probe.Ok ? "200 OK — circuit closed itself, no restarts, no config" : $"still failing: {probe.Error}")}");
Console.WriteLine();

await app.StopAsync();
return;

// ---------- callers ----------

// The retry loop that lives in a thousand codebases (mine included, for years).
static async Task<CallResult> NaiveCall(HttpClient http)
{
    for (var attempt = 1; ; attempt++)
    {
        try
        {
            var resp = await http.GetAsync("/inventory");
            if (resp.IsSuccessStatusCode) return new(true, $"HTTP 200");
            if (attempt == 4) return new(false, $"HTTP {(int)resp.StatusCode}");
        }
        catch (HttpRequestException ex)
        {
            if (attempt == 4) return new(false, ex.GetType().Name);
        }
        // no delay — "just retry it, it's probably transient"
    }
}

// One await; the handler owns retries, backoff, timeouts, and the breaker.
static async Task<CallResult> PipelineCall(HttpClient http)
{
    try
    {
        var resp = await http.GetAsync("/inventory");
        return new(resp.IsSuccessStatusCode, $"HTTP {(int)resp.StatusCode}");
    }
    catch (BrokenCircuitException) { return new(false, "BrokenCircuit (failed fast)"); }
    catch (Exception ex) { return new(false, ex.GetType().Name); }
}

// ---------- harness ----------

async Task RunPhase(string name, double outageSeconds, int calls, Func<Task<CallResult>> call)
{
    state.StartPhase(outageSeconds);
    var sw = Stopwatch.StartNew();
    var results = await Task.WhenAll(
        Enumerable.Range(0, calls).Select(_ => Task.Run(call)));
    sw.Stop();

    var hits = state.Hits.ToArray();
    var durOutage = hits.Count(h => h.DuringOutage);
    var ok = results.Count(r => r.Ok);

    Console.WriteLine($"== {name} ==");
    Console.WriteLine($"client calls : {calls,4}    succeeded: {ok,3}    failed: {calls - ok}");
    Console.WriteLine($"server hits  : {hits.Length,4}    during outage: {durOutage}    after recovery: {hits.Length - durOutage}");
    if (hits.Length > 0)
        Console.WriteLine($"hit window   : first at {hits.Min(h => h.OffsetMs),6:F0} ms, last at {hits.Max(h => h.OffsetMs),6:F0} ms");
    Console.WriteLine($"wall time    : {sw.Elapsed.TotalSeconds:F2} s");
    var reasons = results.Where(r => !r.Ok).GroupBy(r => r.Error)
        .Select(g => $"{g.Key} x{g.Count()}");
    Console.WriteLine($"failures     : {(ok == calls ? "none" : string.Join(", ", reasons))}");
    Console.WriteLine();
}

sealed record CallResult(bool Ok, string? Error);

sealed class FlakyState
{
    private long _outageUntil;
    private long _phaseStart;

    public ConcurrentQueue<(double OffsetMs, bool DuringOutage)> Hits { get; } = new();

    public bool OutageActive => Stopwatch.GetTimestamp() < Volatile.Read(ref _outageUntil);

    public void StartPhase(double outageSeconds)
    {
        Hits.Clear();
        var now = Stopwatch.GetTimestamp();
        Volatile.Write(ref _phaseStart, now);
        Volatile.Write(ref _outageUntil, now + (long)(outageSeconds * Stopwatch.Frequency));
    }

    public void RecordHit(bool duringOutage)
    {
        var offset = (Stopwatch.GetTimestamp() - Volatile.Read(ref _phaseStart)) * 1000.0 / Stopwatch.Frequency;
        Hits.Enqueue((offset, duringOutage));
    }
}
