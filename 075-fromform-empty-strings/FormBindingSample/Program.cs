using System.Net;
using System.Net.Http.Json;
using FormBindingSample;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Mvc;

var builder = WebApplication.CreateBuilder(args);
builder.Logging.ClearProviders();
builder.WebHost.UseUrls("http://127.0.0.1:0");

await using var app = builder.Build();

app.MapPost(
        "/orders",
        ([FromForm] OrderForm form) =>
            TypedResults.Ok(new BindingResult(form.Quantity, form.ShipDate)))
    .DisableAntiforgery();

await app.StartAsync();

var addresses = app.Services
    .GetRequiredService<IServer>()
    .Features
    .Get<IServerAddressesFeature>()
    ?? throw new InvalidOperationException("Kestrel did not expose its listening address.");

using var client = new HttpClient
{
    BaseAddress = new Uri(addresses.Addresses.Single())
};

using var blank = await PostFormAsync(
    client,
    ("Quantity", ""),
    ("ShipDate", ""));

using var valid = await PostFormAsync(
    client,
    ("Quantity", "7"),
    ("ShipDate", "2026-08-24"));
using var malformed = await PostFormAsync(
    client,
    ("Quantity", "seven"),
    ("ShipDate", "2026-08-24"));

var validBody = await ReadBodyAsync(valid);

var checks = new List<(string Name, bool Passed)>
{
    ("valid optional values return HTTP 200", valid.StatusCode == HttpStatusCode.OK),
    ("valid optional values preserve their values", validBody == new BindingResult(7, new DateOnly(2026, 8, 24))),
    ("malformed nonblank value returns HTTP 400", malformed.StatusCode == HttpStatusCode.BadRequest)
};

#if NET10_0_OR_GREATER
var blankBody = await ReadBodyAsync(blank);
checks.Add(("blank optional values return HTTP 200", blank.StatusCode == HttpStatusCode.OK));
checks.Add(("blank optional values bind as null", blankBody == new BindingResult(null, null)));
var expectedBehavior = "blank -> null";
#else
checks.Add(("blank optional values return HTTP 400", blank.StatusCode == HttpStatusCode.BadRequest));
var expectedBehavior = "blank -> 400";
#endif

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var passed = checks.Count(check => check.Passed);
Console.WriteLine($"TARGET {AppContext.TargetFrameworkName} EXPECTED {expectedBehavior}");
Console.WriteLine($"RESULT {passed}/{checks.Count}");

await app.StopAsync();
return passed == checks.Count ? 0 : 1;

static async Task<HttpResponseMessage> PostFormAsync(
    HttpClient client,
    params (string Name, string Value)[] values)
{
    using var form = new FormUrlEncodedContent(
        values.Select(value => new KeyValuePair<string, string>(value.Name, value.Value)));

    return await client.PostAsync("/orders", form);
}

static async Task<BindingResult?> ReadBodyAsync(HttpResponseMessage response) =>
    response.StatusCode == HttpStatusCode.OK
        ? await response.Content.ReadFromJsonAsync<BindingResult>()
        : null;
