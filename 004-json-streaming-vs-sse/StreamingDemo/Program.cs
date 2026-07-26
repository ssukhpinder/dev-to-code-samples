using System.Diagnostics;
using System.Net.ServerSentEvents;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
var app = builder.Build();

const int TotalSteps = 8;
const int DelayMs = 400;
const string Url = "http://127.0.0.1:5177";

// 1) The "obvious" way: return the IAsyncEnumerable, get a JSON array.
app.MapGet("/steps/json", (CancellationToken ct) => Produce(padding: 0, ct));

// 2) Same endpoint, but each item drags ~4 KB of payload with it.
app.MapGet("/steps/json-padded", (CancellationToken ct) => Produce(padding: 4096, ct));

// 3) .NET 10: native Server-Sent Events from the same producer.
app.MapGet("/steps/sse", (CancellationToken ct) =>
    TypedResults.ServerSentEvents(ProduceSse(ct)));

var server = app.RunAsync(Url);

using var http = new HttpClient { BaseAddress = new Uri(Url) };

await Probe(http, "/steps/json");
await Probe(http, "/steps/json-padded");
await Probe(http, "/steps/sse");
await ProbeItems(http, "/steps/json");

await app.StopAsync();
await server;

// Emits one step every DelayMs. This is the "long running job" stand-in.
async IAsyncEnumerable<Step> Produce(int padding, [EnumeratorCancellation] CancellationToken ct = default)
{
    for (var i = 1; i <= TotalSteps; i++)
    {
        await Task.Delay(DelayMs, ct);
        yield return new Step(i, $"step {i}/{TotalSteps}", padding == 0 ? "" : new string('x', padding));
    }
}

async IAsyncEnumerable<SseItem<Step>> ProduceSse([EnumeratorCancellation] CancellationToken ct = default)
{
    await foreach (var step in Produce(padding: 0, ct))
    {
        yield return new SseItem<Step>(step, eventType: "step") { EventId = step.Number.ToString() };
    }
}

// Reads the raw response stream and logs when bytes actually arrive.
async Task Probe(HttpClient client, string path)
{
    Console.WriteLine($"GET {path}");
    var sw = Stopwatch.StartNew();

    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    Console.WriteLine($"  headers    {sw.ElapsedMilliseconds,5} ms   {(int)response.StatusCode} {response.Content.Headers.ContentType?.MediaType}");

    await using var stream = await response.Content.ReadAsStreamAsync();
    var buffer = new byte[32 * 1024];
    int read, reads = 0;
    long total = 0;

    while ((read = await stream.ReadAsync(buffer)) > 0)
    {
        reads++;
        total += read;
        var preview = Encoding.UTF8.GetString(buffer, 0, Math.Min(read, 56)).ReplaceLineEndings("\\n");
        Console.WriteLine($"  read {reads,2}    {sw.ElapsedMilliseconds,5} ms   {read,6} B   {preview}");
    }

    Console.WriteLine($"  done       {sw.ElapsedMilliseconds,5} ms   {total:N0} B in {reads} read(s)");
    Console.WriteLine();
}

// A .NET client does not need SSE to consume the array progressively.
async Task ProbeItems(HttpClient client, string path)
{
    Console.WriteLine($"GET {path} (client: DeserializeAsyncEnumerable)");
    var sw = Stopwatch.StartNew();

    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
    await using var stream = await response.Content.ReadAsStreamAsync();

    await foreach (var step in JsonSerializer.DeserializeAsyncEnumerable<Step>(stream, JsonSerializerOptions.Web))
    {
        Console.WriteLine($"  item {step!.Number}     {sw.ElapsedMilliseconds,5} ms   {step.Name}");
    }

    Console.WriteLine($"  done       {sw.ElapsedMilliseconds,5} ms");
    Console.WriteLine();
}

record Step(int Number, string Name, string Pad);
