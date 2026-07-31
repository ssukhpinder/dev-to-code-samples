using ExceptionPipeline;

// Modes:
//   dotnet run                 -> handler chain on, Production
//   dotnet run -- --baseline   -> no exception handling configured, Production
//   dotnet run -- --baseline --dev -> no handling, Development (developer exception page)
//   dotnet run -- --swapped    -> chain registered in the wrong order (fallback first)
var baseline = args.Contains("--baseline");
var swapped = args.Contains("--swapped");
var dev = args.Contains("--dev");

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = dev ? Environments.Development : Environments.Production
});
builder.Logging.AddFilter("Microsoft.AspNetCore.Hosting.Diagnostics", LogLevel.None);
builder.Logging.AddFilter("Microsoft.AspNetCore.Routing", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.AspNetCore.Http.Result", LogLevel.Warning);
builder.Logging.AddFilter("Microsoft.Hosting.Lifetime", LogLevel.Warning);
builder.WebHost.UseUrls("http://127.0.0.1:5090");

if (!baseline)
{
    builder.Services.AddProblemDetails();
    if (swapped)
    {
        builder.Services.AddExceptionHandler<UnhandledExceptionHandler>(); // wrong: greedy fallback first
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();    // never reached
    }
    else
    {
        builder.Services.AddExceptionHandler<DomainExceptionHandler>();    // specific first
        builder.Services.AddExceptionHandler<UnhandledExceptionHandler>(); // fallback last
    }
}

var app = builder.Build();

if (!baseline)
    app.UseExceptionHandler();

// Endpoints throw freely; nothing here knows about status codes.
app.MapGet("/products/{id:int}", (int id) => id == 1
    ? Results.Ok(new Product(1, "Mechanical Keyboard", 89.99m))
    : throw new ProductNotFoundException(id));

app.MapPost("/products/{id:int}/reserve", (int id) =>
{
    if (id == 7)
        throw new StaleInventoryException(id, expected: 12, actual: 3);
    throw new InvalidOperationException(
        "Inventory shard 3 returned row version 0xDEADBEEF at offset 128; retry token missing.");
});

await app.StartAsync();

// Probe our own endpoints and print the raw exchanges.
using var http = new HttpClient { BaseAddress = new Uri("http://127.0.0.1:5090") };

await Probe(HttpMethod.Get, "/products/1");
await Probe(HttpMethod.Get, "/products/999");
await Probe(HttpMethod.Post, "/products/7/reserve");
await Probe(HttpMethod.Post, "/products/2/reserve");

await app.StopAsync();

async Task Probe(HttpMethod method, string path)
{
    using var response = await http.SendAsync(new HttpRequestMessage(method, path));
    var body = await response.Content.ReadAsStringAsync();
    if (body.Length > 400) body = body[..400] + $"... [{body.Length} chars total]";
    Console.WriteLine($"\n>> {method} {path}");
    Console.WriteLine($"<< {(int)response.StatusCode} {response.StatusCode}  " +
        $"content-type: {response.Content.Headers.ContentType?.ToString() ?? "(none)"}  " +
        $"bytes: {response.Content.Headers.ContentLength?.ToString() ?? "?"}");
    if (body.Length > 0) Console.WriteLine(body);
}
