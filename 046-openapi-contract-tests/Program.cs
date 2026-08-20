using System.Text.Json;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.AspNetCore.TestHost;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseTestServer();
builder.Services.AddOpenApi("contract");

await using var app = builder.Build();

app.MapGet(
        "/orders/{id:int}",
        (int id) => TypedResults.Ok(new Order(id, "ready")))
    .WithName("GetOrder")
    .Produces<Order>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status404NotFound);

app.MapPost(
        "/orders",
        (CreateOrder request) =>
            TypedResults.Created($"/orders/{request.CustomerId}", new Order(request.CustomerId, "queued")))
    .WithName("CreateOrder")
    .Accepts<CreateOrder>("application/json")
    .Produces<Order>(StatusCodes.Status201Created)
    .ProducesValidationProblem();

await app.StartAsync();

IOpenApiDocumentProvider provider =
    app.Services.GetRequiredKeyedService<IOpenApiDocumentProvider>("contract");

OpenApiDocument document = await provider.GetOpenApiDocumentAsync();
string documentJson = await document.SerializeAsJsonAsync(OpenApiSpecVersion.OpenApi3_1);

using JsonDocument parsed = JsonDocument.Parse(documentJson);
JsonElement root = parsed.RootElement;
JsonElement paths = root.GetProperty("paths");
JsonElement getOrder = paths.GetProperty("/orders/{id}").GetProperty("get");
JsonElement createOrder = paths.GetProperty("/orders").GetProperty("post");

var verifier = new Verifier();

verifier.Check(
    root.GetProperty("openapi").GetString()?.StartsWith("3.1", StringComparison.Ordinal) is true,
    "document uses OpenAPI 3.1");
verifier.Check(
    paths.EnumerateObject().Select(path => path.Name).Order().SequenceEqual(["/orders", "/orders/{id}"]),
    "only the expected paths are present");
verifier.Check(
    getOrder.GetProperty("operationId").GetString() == "GetOrder",
    "GET operation ID is stable");
verifier.Check(
    HasRequiredPathParameter(getOrder, "id"),
    "GET order ID remains a required path parameter");
verifier.Check(
    HasResponses(getOrder, "200", "404"),
    "GET advertises 200 and 404 responses");
verifier.Check(
    createOrder.GetProperty("operationId").GetString() == "CreateOrder",
    "POST operation ID is stable");
verifier.Check(
    createOrder.GetProperty("requestBody").GetProperty("required").GetBoolean(),
    "POST request body remains required");
verifier.Check(
    HasResponses(createOrder, "201", "400"),
    "POST advertises 201 and 400 responses");

await app.StopAsync();
verifier.Complete();

static bool HasRequiredPathParameter(JsonElement operation, string name) =>
    operation
        .GetProperty("parameters")
        .EnumerateArray()
        .Any(parameter =>
            parameter.GetProperty("name").GetString() == name
            && parameter.GetProperty("in").GetString() == "path"
            && parameter.GetProperty("required").GetBoolean());

static bool HasResponses(JsonElement operation, params string[] expected) =>
    expected.All(status => operation.GetProperty("responses").TryGetProperty(status, out _));

internal sealed record Order(int Id, string Status);

internal sealed record CreateOrder(int CustomerId);

internal sealed class Verifier
{
    private int _passed;
    private int _failed;

    public void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS {name}");
            return;
        }

        _failed++;
        Console.WriteLine($"FAIL {name}");
    }

    public void Complete()
    {
        Console.WriteLine($"{_passed}/{_passed + _failed} checks passed.");

        if (_failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
