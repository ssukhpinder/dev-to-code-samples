using System.ComponentModel.DataAnnotations;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);

// The one line this whole demo is about (.NET 10):
builder.Services.AddValidation();

var app = builder.Build();

// ---------- the "before": validation I wrote by hand ----------
// .DisableValidation() keeps the framework out so these checks actually run.
// (First time I ran this demo WITHOUT it, AddValidation rejected the request
// before my handler was ever called — my checks were already dead code.)
app.MapPost("/manual/products", (CreateProduct dto) =>
{
    var errors = new Dictionary<string, string[]>();
    if (string.IsNullOrWhiteSpace(dto.Name))
        errors["Name"] = ["Name is required."];
    else if (dto.Name.Length > 80)
        errors["Name"] = ["Name must be 80 characters or fewer."];
    if (dto.Price is < 0.01m or > 10_000m)
        errors["Price"] = ["Price must be between 0.01 and 10000."];
    if (dto.SalePrice is { } sale && sale >= dto.Price)
        errors["SalePrice"] = ["SalePrice must be lower than Price."];
    if (errors.Count > 0)
        return Results.ValidationProblem(errors);

    return Results.Created($"/products/1", dto);
}).DisableValidation();

// ---------- the "after": same rules, zero checks in the handler ----------
app.MapPost("/products", (CreateProduct dto) =>
    Results.Created($"/products/1", dto));

// query parameters get validated too
app.MapGet("/search", ([Range(1, 100)] int pageSize = 20) =>
    Results.Ok(new { pageSize }));

// the escape hatch
app.MapPost("/legacy/products", (CreateProduct dto) =>
        Results.Created($"/products/1", dto))
    .DisableValidation();

// ---------- self-probe: start the app, hit it, print what happened ----------
await app.StartAsync();
var baseUrl = app.Urls.First();
using var http = new HttpClient { BaseAddress = new Uri(baseUrl) };

var garbage = """{"name":"","price":-5,"salePrice":3}""";
var crossField = """{"name":"Desk lamp","price":40,"salePrice":95}""";
var valid = """{"name":"Desk lamp","price":40,"salePrice":25}""";

await Probe("POST /manual/products", "garbage in", () => PostJson("/manual/products", garbage));
await Probe("POST /products       ", "garbage in", () => PostJson("/products", garbage));
await Probe("POST /products       ", "sale > price", () => PostJson("/products", crossField));
await Probe("POST /products       ", "valid", () => PostJson("/products", valid));
await Probe("GET  /search?pageSize=0", "bad query param", () => http.GetAsync("/search?pageSize=0"));
await Probe("POST /legacy/products", "garbage in, validation off", () => PostJson("/legacy/products", garbage));

await app.StopAsync();

Task<HttpResponseMessage> PostJson(string url, string json) =>
    http.PostAsync(url, new StringContent(json, System.Text.Encoding.UTF8, "application/json"));

async Task Probe(string label, string scenario, Func<Task<HttpResponseMessage>> send)
{
    var resp = await send();
    var body = await resp.Content.ReadAsStringAsync();
    Console.WriteLine($"{label}  [{scenario}]");
    Console.WriteLine($"  -> {(int)resp.StatusCode} {resp.StatusCode}");
    if (!string.IsNullOrWhiteSpace(body))
        Console.WriteLine($"  {Compact(body)}");
    Console.WriteLine();
}

static string Compact(string json)
{
    try
    {
        using var doc = JsonDocument.Parse(json);
        return JsonSerializer.Serialize(doc.RootElement);
    }
    catch { return json; }
}

// One DTO, rules declared once, in the type
public record CreateProduct(
    [property: Required, StringLength(80)] string Name,
    [property: Range(0.01, 10_000)] decimal Price,
    decimal? SalePrice) : IValidatableObject
{
    public IEnumerable<ValidationResult> Validate(ValidationContext _)
    {
        if (SalePrice is { } sale && sale >= Price)
            yield return new ValidationResult(
                "SalePrice must be lower than Price.", [nameof(SalePrice)]);
    }
}
