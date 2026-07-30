// Demo: the fixed-window "seam" — a rate limit of 10 requests / 10 seconds
// admits nearly double that inside a single 10-second span when a burst
// straddles the window boundary. The sliding-window limiter, same numbers,
// doesn't have the seam.
//
// The app starts a minimal API on a random port, then attacks its own
// endpoints with HttpClient using boundary-straddling choreography:
//   1. burn the current window down to zero
//   2. poll until a request succeeds again -> that's a replenish boundary
//   3. go quiet, come back just before the NEXT boundary, burst 9
//   4. step over the boundary, burst 10 more
// Everything is timestamped; at the end it reports the densest 10-second
// span the limiter actually allowed.

using System.Diagnostics;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:0");

const int PermitLimit = 10;
static TimeSpan Window() => TimeSpan.FromSeconds(10);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // The one everybody reaches for first.
    options.AddFixedWindowLimiter("fixed", o =>
    {
        o.PermitLimit = PermitLimit;
        o.Window = Window();
        o.QueueLimit = 0;               // reject immediately, no queueing
    });

    // Same budget, but the window slides in 2-second segments.
    options.AddSlidingWindowLimiter("sliding", o =>
    {
        o.PermitLimit = PermitLimit;
        o.Window = Window();
        o.SegmentsPerWindow = 5;        // 5 x 2s segments
        o.QueueLimit = 0;
    });
});

var app = builder.Build();
app.UseRateLimiter();

app.MapGet("/fixed", () => Results.Ok(new { ok = true })).RequireRateLimiting("fixed");
app.MapGet("/sliding", () => Results.Ok(new { ok = true })).RequireRateLimiting("sliding");

await app.StartAsync();
var baseUrl = app.Urls.First();
Console.WriteLine($"Server up at {baseUrl}. Both policies: {PermitLimit} permits per {Window().TotalSeconds:F0}s, QueueLimit 0.\n");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

foreach (var path in new[] { "/fixed", "/sliding" })
{
    var admitted = await Attack(path);
    Report(path, admitted);
}

await app.StopAsync();

// ---------------------------------------------------------------------------

async Task<List<double>> Attack(string path)
{
    Console.WriteLine($"--- attacking {path} ---");
    var clock = Stopwatch.StartNew();
    var admitted = new List<double>();   // timestamps (s) of every 200

    async Task<bool> Hit()
    {
        var t = clock.Elapsed.TotalSeconds;
        using var resp = await http.GetAsync(path);
        if (resp.IsSuccessStatusCode) { admitted.Add(t); return true; }
        return false;
    }

    // 1. burn the current window down to zero
    int burned = 0;
    for (var i = 0; i < PermitLimit + 2; i++) if (await Hit()) burned++;
    Console.WriteLine($"  burn phase: {burned} admitted, then 429s — window is empty");

    // 2. poll gently until a request lands again: a replenish boundary
    double flip;
    while (true)
    {
        await Task.Delay(200);
        if (await Hit()) { flip = admitted[^1]; break; }
    }
    Console.WriteLine($"  permits came back at t={flip:F1}s — boundary located");

    // 3. sit quiet, return 1s before the NEXT boundary, spend the window
    var boundary = flip + Window().TotalSeconds;
    await SleepUntil(clock, boundary - 1.0);
    int pre = 0;
    var preStart = clock.Elapsed.TotalSeconds;
    for (var i = 0; i < PermitLimit - 1; i++) if (await Hit()) pre++;
    Console.WriteLine($"  pre-boundary burst  at t={preStart:F1}s: {pre}/{PermitLimit - 1} admitted");

    // 4. step over the boundary, ask for a fresh window's worth
    await SleepUntil(clock, boundary + 0.3);
    int post = 0;
    var postStart = clock.Elapsed.TotalSeconds;
    for (var i = 0; i < PermitLimit; i++) if (await Hit()) post++;
    Console.WriteLine($"  post-boundary burst at t={postStart:F1}s: {post}/{PermitLimit} admitted");

    return admitted;
}

static async Task SleepUntil(Stopwatch clock, double t)
{
    var wait = t - clock.Elapsed.TotalSeconds;
    if (wait > 0) await Task.Delay(TimeSpan.FromSeconds(wait));
}

void Report(string path, List<double> admitted)
{
    // densest run of admitted requests inside any 10-second span
    admitted.Sort();
    int best = 0, lo = 0; double span = 0;
    for (var hi = 0; hi < admitted.Count; hi++)
    {
        while (admitted[hi] - admitted[lo] >= Window().TotalSeconds) lo++;
        var count = hi - lo + 1;
        if (count > best) { best = count; span = admitted[hi] - admitted[lo]; }
    }
    Console.WriteLine($"  => {path}: densest 10s span held {best} admitted requests" +
                      $" (limit says {PermitLimit}); those {best} arrived within {Math.Max(span, 0.01):F2}s\n");
}
