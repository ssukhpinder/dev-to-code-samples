using System.Diagnostics;
using Microsoft.AspNetCore.Http.Timeouts;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:0");

// Global default: every endpoint gets a 2-second budget.
builder.Services.AddRequestTimeouts(options =>
{
    options.DefaultPolicy = new RequestTimeoutPolicy
    {
        Timeout = TimeSpan.FromSeconds(2)
    };
});

var app = builder.Build();
app.UseRequestTimeouts();

// Simulated slow dependency: ~6 seconds of "database work".
static async Task<IResult> SlowWork(CancellationToken ct)
{
    await Task.Delay(TimeSpan.FromSeconds(6), ct);
    return Results.Json(new { done = true });
}

// 1. Honors the token: the 6s delay observes cancellation.
app.MapGet("/honors-token", (CancellationToken ct) => SlowWork(ct));

// 2. Ignores the token: same 6s of work, token never consulted.
app.MapGet("/ignores-token", () => SlowWork(CancellationToken.None));

// 3. Same handler, tighter per-endpoint budget.
app.MapGet("/tight", (CancellationToken ct) => SlowWork(ct))
   .WithRequestTimeout(TimeSpan.FromMilliseconds(500));

// 4. Same handler, timeout deliberately switched off (think: long export).
app.MapGet("/opted-out", (CancellationToken ct) => SlowWork(ct))
   .DisableRequestTimeout();

await app.StartAsync();
var baseUrl = app.Urls.First();
Console.WriteLine($"Server up at {baseUrl}. Default timeout: 2s. Simulated work: 6s.\n");

using var http = new HttpClient { BaseAddress = new Uri(baseUrl), Timeout = TimeSpan.FromSeconds(30) };

async Task Probe(string path, string note)
{
    var sw = Stopwatch.StartNew();
    using var resp = await http.GetAsync(path);
    sw.Stop();
    Console.WriteLine($"{path,-15} -> {(int)resp.StatusCode} {resp.StatusCode,-16} after {sw.Elapsed.TotalSeconds:F2}s   {note}");
}

await Probe("/honors-token", "handler passed the token along");
await Probe("/tight", "per-endpoint 500ms policy");
await Probe("/ignores-token", "handler ignored the token");
await Probe("/opted-out", "DisableRequestTimeout()");

await app.StopAsync();
