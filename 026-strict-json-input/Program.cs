using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.TestHost;

const string ambiguousPayload =
    """{"displayName":"Ada","role":"reader","role":"admin","isAdmin":true}""";

var relaxed = JsonSerializer.Deserialize<CreateUser>(
    ambiguousPayload,
    JsonSerializerOptions.Web)
    ?? throw new InvalidOperationException(
        "Web defaults should deserialize the baseline payload.");

Check(relaxed.Role == "admin", "Web defaults should keep the last duplicate value.");
Console.WriteLine("BASELINE web defaults: duplicate role became 'admin'; isAdmin was ignored");

var builder = WebApplication.CreateBuilder(args);
builder.Environment.EnvironmentName = Environments.Production;
builder.WebHost.UseTestServer();
builder.Logging.ClearProviders();

builder.Services.ConfigureHttpJsonOptions(options =>
{
    var json = options.SerializerOptions;
    json.AllowDuplicateProperties = false;
    json.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
    json.PropertyNameCaseInsensitive = false;
    json.RespectNullableAnnotations = true;
    json.RespectRequiredConstructorParameters = true;
});

var app = builder.Build();
var handledRequests = 0;

app.MapPost("/users", (CreateUser request) =>
{
    Interlocked.Increment(ref handledRequests);
    return TypedResults.Ok(request);
});

await app.StartAsync();
using var client = app.GetTestClient();

var cases = new[]
{
    new RequestCase(
        "valid payload",
        """{"displayName":"Ada","role":"reader"}""",
        HttpStatusCode.OK),
    new RequestCase(
        "duplicate property",
        """{"displayName":"Ada","role":"reader","role":"admin"}""",
        HttpStatusCode.BadRequest),
    new RequestCase(
        "unmapped property",
        """{"displayName":"Ada","role":"reader","isAdmin":true}""",
        HttpStatusCode.BadRequest),
    new RequestCase(
        "null for non-nullable member",
        """{"displayName":null,"role":"reader"}""",
        HttpStatusCode.BadRequest),
    new RequestCase(
        "missing constructor parameter",
        """{"displayName":"Ada"}""",
        HttpStatusCode.BadRequest),
    new RequestCase(
        "wrong property casing",
        """{"DisplayName":"Ada","role":"reader"}""",
        HttpStatusCode.BadRequest)
};

foreach (var testCase in cases)
{
    using var content = new StringContent(
        testCase.Json,
        Encoding.UTF8,
        "application/json");
    using var response = await client.PostAsync("/users", content);

    Check(
        response.StatusCode == testCase.ExpectedStatus,
        $"{testCase.Name}: expected {(int)testCase.ExpectedStatus}, " +
        $"received {(int)response.StatusCode}.");

    Console.WriteLine(
        $"PASS {testCase.Name}: {(int)response.StatusCode} {response.StatusCode}");
}

Check(handledRequests == 1, "Only the valid payload should reach the handler.");
Console.WriteLine($"VERIFIED {cases.Length}/{cases.Length} HTTP cases; handler calls={handledRequests}");

await app.StopAsync();
return 0;

static void Check(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

internal sealed record CreateUser(string DisplayName, string Role);

internal sealed record RequestCase(
    string Name,
    string Json,
    HttpStatusCode ExpectedStatus);
