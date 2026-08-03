using System.Diagnostics;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);

builder.Services.Configure<FeatureOptions>(builder.Configuration.GetSection("Features"));
builder.Services.AddSingleton<ExportService>();

var app = builder.Build();

// Same section, read through three different interfaces.
app.MapGet("/options", (IOptions<FeatureOptions> o) =>
    Show("IOptions", o.Value));

app.MapGet("/snapshot", (IOptionsSnapshot<FeatureOptions> o) =>
    Show("IOptionsSnapshot", o.Value));

app.MapGet("/monitor", (IOptionsMonitor<FeatureOptions> o) =>
    Show("IOptionsMonitor", o.CurrentValue));

// The sneaky one: a singleton that injected IOptionsMonitor but froze
// CurrentValue into a field at construction time.
app.MapGet("/frozen", (ExportService s) =>
    Show("frozen singleton", s.Frozen));

// Rough cost check: what does per-scope re-binding actually cost?
app.MapGet("/bench", (IServiceProvider sp, IOptionsMonitor<FeatureOptions> monitor) =>
{
    const int N = 100_000;

    // warmup
    using (var s = sp.CreateScope())
        _ = s.ServiceProvider.GetRequiredService<IOptionsSnapshot<FeatureOptions>>().Value;

    var sw = Stopwatch.StartNew();
    for (var i = 0; i < N; i++)
        using (var s = sp.CreateScope()) { }
    var emptyScopeMs = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    for (var i = 0; i < N; i++)
        using (var s = sp.CreateScope())
            _ = s.ServiceProvider.GetRequiredService<IOptionsSnapshot<FeatureOptions>>().Value;
    var snapshotMs = sw.Elapsed.TotalMilliseconds;

    sw.Restart();
    for (var i = 0; i < N; i++)
        _ = monitor.CurrentValue;
    var monitorMs = sw.Elapsed.TotalMilliseconds;

    return $"{N:N0} iterations:\n" +
           $"  empty scope           {emptyScopeMs,8:F1} ms\n" +
           $"  scope + snapshot bind {snapshotMs,8:F1} ms\n" +
           $"  monitor.CurrentValue  {monitorMs,8:F1} ms\n";
});

// Log every reload the change-token pipeline reports.
var changeLog = app.Services.GetRequiredService<IOptionsMonitor<FeatureOptions>>();
changeLog.OnChange(o =>
    Console.WriteLine($"[reload] Features changed: ExportEnabled={o.ExportEnabled}"));

app.Run();

static string Show(string via, FeatureOptions o) =>
    $"{via,-18} ExportEnabled={o.ExportEnabled} MaxPageSize={o.MaxPageSize}\n";

public sealed class FeatureOptions
{
    public bool ExportEnabled { get; set; }
    public int MaxPageSize { get; set; } = 50;
}

// The bug I actually shipped: "I injected IOptionsMonitor, so I'm safe" —
// except the constructor reads CurrentValue once and keeps the copy forever.
public sealed class ExportService(IOptionsMonitor<FeatureOptions> monitor)
{
    public FeatureOptions Frozen { get; } = monitor.CurrentValue;
}
