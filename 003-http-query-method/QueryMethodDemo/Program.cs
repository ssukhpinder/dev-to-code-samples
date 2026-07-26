using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.OutputCaching;

// ---------------------------------------------------------------------------
// A tiny catalog service that exposes the SAME search operation three ways:
//   GET   /products/search?sku=...&sku=...      (filters in the URL)
//   POST  /products/search                      (filters in the body, unsafe)
//   QUERY /products/search                      (filters in the body, safe)
// The app boots, runs a small harness against itself, prints results, exits.
// ---------------------------------------------------------------------------

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:5199");
builder.Services.AddOutputCache();

var app = builder.Build();
app.UseOutputCache();

// Pretend catalog: sku-000001 .. sku-005000
var catalog = Enumerable.Range(1, 5000)
    .ToDictionary(i => $"sku-{i:D6}", i => new Product($"sku-{i:D6}", 5m + (i % 400)));

// How many times the search actually executed on the server. If a response is
// served from a cache this number will not move.
var executions = 0;

SearchResult RunSearch(string[] skus, decimal? maxPrice)
{
    var n = Interlocked.Increment(ref executions);
    var hits = skus.Where(s => catalog.TryGetValue(s, out var p)
                               && (maxPrice is null || p.Price <= maxPrice))
                   .ToArray();
    return new SearchResult(hits.Length, n);
}

// --- GET: filters have to live in the URL ----------------------------------
app.MapGet("/products/search", (HttpContext ctx) =>
{
    var skus = ctx.Request.Query["sku"].Select(s => s!).ToArray();
    decimal? maxPrice = decimal.TryParse(ctx.Request.Query["maxPrice"], out var mp) ? mp : null;
    return Results.Ok(RunSearch(skus, maxPrice));
}).CacheOutput();

// --- POST: body works, but the method is neither safe nor cacheable --------
app.MapPost("/products/search", async (HttpContext ctx) =>
{
    var filter = await JsonSerializer.DeserializeAsync<Filter>(ctx.Request.Body);
    return Results.Ok(RunSearch(filter!.Skus, filter.MaxPrice));
}).CacheOutput();

// --- QUERY: body + safe + idempotent --------------------------------------
// There is no MapQuery in ASP.NET Core 10, so MapMethods is the way in.
app.MapMethods("/products/search", [HttpMethods.Query], async (HttpContext ctx) =>
{
    // RFC 10008: "Servers MUST fail the request if the Content-Type request
    // field is missing or is inconsistent with the request content."
    if (string.IsNullOrEmpty(ctx.Request.ContentType))
        return Results.Problem("QUERY requires a Content-Type.", statusCode: 400);
    if (!ctx.Request.ContentType.StartsWith("application/json", StringComparison.OrdinalIgnoreCase))
        return Results.StatusCode(StatusCodes.Status415UnsupportedMediaType);

    var filter = await JsonSerializer.DeserializeAsync<Filter>(ctx.Request.Body);
    var result = RunSearch(filter!.Skus, filter.MaxPrice);

    // Content-Location points at a GET-able resource holding these results.
    ctx.Response.Headers.ContentLocation = $"/products/search-results/{result.Execution}";
    return Results.Ok(result);
}).CacheOutput();

// --- QUERY, but with a hand-written policy that opts it into caching ------
// Looks like the fix. It is not. See harness step 5.
app.MapMethods("/products/search-cached", [HttpMethods.Query], async (HttpContext ctx) =>
{
    var filter = await JsonSerializer.DeserializeAsync<Filter>(ctx.Request.Body);
    return Results.Ok(RunSearch(filter!.Skus, filter.MaxPrice));
}).CacheOutput(b => b.AddPolicy<CacheQueryToo>());

await app.StartAsync();

// ===========================================================================
// Harness
// ===========================================================================
const string Base = "http://127.0.0.1:5199";
var http = new HttpClient { BaseAddress = new Uri(Base) };

Console.WriteLine($".NET {Environment.Version}");
Console.WriteLine($"HttpMethod.Query      -> {HttpMethod.Query.Method}");
Console.WriteLine($"HttpMethods.IsQuery   -> {HttpMethods.IsQuery("query")} (lowercase input)");
Console.WriteLine();

// --- 1. How many filters fit in a GET before the URL falls over? -----------
Console.WriteLine("1) GET with filters in the URL");
for (var count = 100; count <= 900; count += 100)
{
    var url = $"{Base}/products/search?" + string.Join("&", Skus(count).Select(s => $"sku={s}"));
    HttpStatusCode status;
    try
    {
        using var resp = await http.GetAsync(url);
        status = resp.StatusCode;
    }
    catch (HttpRequestException)
    {
        status = 0;
    }
    Console.WriteLine($"   {count,4} filters | request line {url.Length - Base.Length + 18,5} bytes | {(int)status} {status}");
    if ((int)status >= 400 || status == 0) break;
}
Console.WriteLine();

// --- 2. The same operation, 5000 filters, as a QUERY body -----------------
Console.WriteLine("2) QUERY with filters in the body");
var big = JsonSerializer.Serialize(new Filter(Skus(5000), 300m));
using (var req = new HttpRequestMessage(HttpMethod.Query, "/products/search"))
{
    req.Content = new StringContent(big, Encoding.UTF8, "application/json");
    using var resp = await http.SendAsync(req);
    var body = await resp.Content.ReadAsStringAsync();
    Console.WriteLine($"   body {big.Length} bytes | {(int)resp.StatusCode} {resp.StatusCode} | {body}");
    Console.WriteLine($"   Content-Location: {resp.Content.Headers.ContentLocation}");
}
Console.WriteLine();

// --- 3. Content-Type rules -------------------------------------------------
Console.WriteLine("3) Content-Type handling");
Console.WriteLine($"   no Content-Type   -> {await StatusOf(null)}");
Console.WriteLine($"   text/plain        -> {await StatusOf("text/plain")}");
Console.WriteLine($"   application/json  -> {await StatusOf("application/json")}");
Console.WriteLine();

// --- 4. Does output caching treat QUERY as cacheable? ---------------------
Console.WriteLine("4) Output caching (execution number only moves on a real hit)");
Console.WriteLine($"   GET   x3 -> {await Executions(GetReq)}");
Console.WriteLine($"   POST  x3 -> {await Executions(() => BodyReq(HttpMethod.Post))}");
Console.WriteLine($"   QUERY x3 -> {await Executions(() => BodyReq(HttpMethod.Query))}");
Console.WriteLine();

// --- 5. Forcing QUERY into the output cache: what breaks ------------------
Console.WriteLine("5) Custom policy that caches QUERY (same URL, two different bodies)");
Console.WriteLine($"   body A (2 skus)  -> {await Cached(["sku-000001", "sku-000002"])}");
Console.WriteLine($"   body B (4 skus)  -> {await Cached(["sku-000001", "sku-000002", "sku-000003", "sku-000004"])}");

await app.StopAsync();
return;

// ---------------------------------------------------------------------------
static string[] Skus(int n) => Enumerable.Range(1, n).Select(i => $"sku-{i:D6}").ToArray();

static HttpRequestMessage GetReq() =>
    new(HttpMethod.Get, "/products/search?sku=sku-000001&sku=sku-000002&maxPrice=300");

static HttpRequestMessage BodyReq(HttpMethod method) => new(method, "/products/search")
{
    Content = new StringContent(
        JsonSerializer.Serialize(new Filter(["sku-000001", "sku-000002"], 300m)),
        Encoding.UTF8, "application/json")
};

async Task<string> StatusOf(string? contentType)
{
    using var req = new HttpRequestMessage(HttpMethod.Query, "/products/search");
    var content = new StringContent("""{"Skus":["sku-000001"],"MaxPrice":300}""");
    content.Headers.Remove("Content-Type");
    if (contentType is not null) content.Headers.TryAddWithoutValidation("Content-Type", contentType);
    req.Content = content;
    using var resp = await http.SendAsync(req);
    return $"{(int)resp.StatusCode} {resp.StatusCode}";
}

async Task<string> Executions(Func<HttpRequestMessage> factory)
{
    var web = new JsonSerializerOptions(JsonSerializerDefaults.Web);
    var seen = new List<string>();
    for (var i = 0; i < 3; i++)
    {
        using var req = factory();
        using var resp = await http.SendAsync(req);
        var json = await resp.Content.ReadAsStringAsync();
        seen.Add(resp.IsSuccessStatusCode
            ? JsonSerializer.Deserialize<SearchResult>(json, web)!.Execution.ToString()
            : $"({(int)resp.StatusCode})");
    }
    var cached = seen.Distinct().Count() == 1 && !seen[0].StartsWith('(');
    return $"executions {string.Join(", ", seen)}  => {(cached ? "cached" : "NOT cached")}";
}

async Task<string> Cached(string[] skus)
{
    using var req = new HttpRequestMessage(HttpMethod.Query, "/products/search-cached")
    {
        Content = new StringContent(JsonSerializer.Serialize(new Filter(skus, 300m)),
                                    Encoding.UTF8, "application/json")
    };
    using var resp = await http.SendAsync(req);
    return await resp.Content.ReadAsStringAsync();
}

sealed class CacheQueryToo : IOutputCachePolicy
{
    public ValueTask CacheRequestAsync(OutputCacheContext context, CancellationToken token)
    {
        var method = context.HttpContext.Request.Method;
        var attempt = HttpMethods.IsGet(method) || HttpMethods.IsHead(method)
                                                || HttpMethods.IsQuery(method);
        context.EnableOutputCaching = attempt;
        context.AllowCacheLookup = attempt;
        context.AllowCacheStorage = attempt;
        context.AllowLocking = true;
        context.CacheVaryByRules.QueryKeys = "*";
        return ValueTask.CompletedTask;
    }

    public ValueTask ServeFromCacheAsync(OutputCacheContext context, CancellationToken token)
        => ValueTask.CompletedTask;

    public ValueTask ServeResponseAsync(OutputCacheContext context, CancellationToken token)
    {
        if (context.HttpContext.Response.StatusCode != StatusCodes.Status200OK)
            context.AllowCacheStorage = false;
        return ValueTask.CompletedTask;
    }
}

record Product(string Sku, decimal Price);
record Filter(string[] Skus, decimal? MaxPrice);
record SearchResult(int Matches, int Execution);
