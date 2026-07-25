using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Caching.Memory;

// Self-contained stampede demo: starts a minimal API, then hammers it with
// concurrent requests to show how many times each caching strategy actually
// calls the "database" for a single cold key.

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:0"); // random free port

builder.Services.AddSingleton<FakeDb>();
builder.Services.AddMemoryCache();
builder.Services.AddHybridCache();

var app = builder.Build();

// The "before" endpoint: classic IMemoryCache.GetOrCreateAsync.
// Note the "mem:" prefix — HybridCache's L1 is the SAME MemoryCache instance,
// so sharing raw keys between the two causes an InvalidCastException.
app.MapGet("/products/memory/{id}", async (string id, IMemoryCache cache, FakeDb db) =>
    await cache.GetOrCreateAsync($"mem:product:{id}", entry =>
    {
        entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(5);
        return db.LoadProductAsync(id, flavor: "memory");
    }));

// The "after" endpoint: HybridCache with built-in stampede protection.
app.MapGet("/products/hybrid/{id}", async (string id, HybridCache cache, FakeDb db, CancellationToken ct) =>
    await cache.GetOrCreateAsync(
        $"hyb:product:{id}",
        async token => await db.LoadProductAsync(id, flavor: "hybrid"),
        new HybridCacheEntryOptions { Expiration = TimeSpan.FromMinutes(5) },
        cancellationToken: ct));

await app.StartAsync();

var baseUrl = app.Urls.First();
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };
var db = app.Services.GetRequiredService<FakeDb>();

const int Clients = 50;

async Task<(int DbCalls, long ElapsedMs)> BurstAsync(string path, string flavor)
{
    db.Reset(flavor);
    var sw = Stopwatch.StartNew();
    var tasks = Enumerable.Range(0, Clients).Select(_ => http.GetStringAsync(path));
    await Task.WhenAll(tasks);
    sw.Stop();
    return (db.Calls(flavor), sw.ElapsedMilliseconds);
}

// Warm up Kestrel, the JSON serializer and the JIT with a throwaway id so the
// measured bursts aren't paying first-request overhead.
await BurstAsync("/products/memory/warmup", "memory");
await BurstAsync("/products/hybrid/warmup", "hybrid");

Console.WriteLine($"Firing {Clients} concurrent requests per endpoint (cold cache), fake query takes ~200 ms\n");

var memCold = await BurstAsync("/products/memory/42", "memory");
var hybCold = await BurstAsync("/products/hybrid/42", "hybrid");

Console.WriteLine($"IMemoryCache  cold burst: {memCold.DbCalls,2} db calls, {memCold.ElapsedMs,4} ms wall");
Console.WriteLine($"HybridCache   cold burst: {hybCold.DbCalls,2} db calls, {hybCold.ElapsedMs,4} ms wall");

// Second wave while the cache is warm — both should be quiet now.
var memWarm = await BurstAsync("/products/memory/42", "memory");
var hybWarm = await BurstAsync("/products/hybrid/42", "hybrid");

Console.WriteLine();
Console.WriteLine($"IMemoryCache  warm burst: {memWarm.DbCalls,2} db calls, {memWarm.ElapsedMs,4} ms wall");
Console.WriteLine($"HybridCache   warm burst: {hybWarm.DbCalls,2} db calls, {hybWarm.ElapsedMs,4} ms wall");

await app.StopAsync();

// Pretend database: counts every query and takes ~200 ms, like a real one under load.
sealed class FakeDb
{
    private readonly ConcurrentDictionary<string, int> _calls = new();

    public async Task<Product> LoadProductAsync(string id, string flavor)
    {
        _calls.AddOrUpdate(flavor, 1, (_, current) => current + 1);
        await Task.Delay(200); // stand-in for a slow query
        return new Product(id, $"Product {id}", 19.99m);
    }

    public void Reset(string flavor) => _calls[flavor] = 0;

    public int Calls(string flavor) => _calls.GetValueOrDefault(flavor);
}

public sealed record Product(string Id, string Name, decimal Price);
