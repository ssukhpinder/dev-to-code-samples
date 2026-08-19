using System.Collections.Concurrent;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Options;

const string scheme = "DemoToken";

using var metrics = new AuthenticationMetricCollector();

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseTestServer();
builder.Logging.ClearProviders();
builder.Services
    .AddAuthentication(scheme)
    .AddScheme<AuthenticationSchemeOptions, DemoTokenHandler>(scheme, _ => { });
builder.Services.AddAuthorization();

await using var app = builder.Build();
app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/public", () => Results.Ok(new { status = "anonymous" }));
app.MapGet(
        "/private",
        (ClaimsPrincipal principal) => Results.Ok(new { user = principal.Identity?.Name }))
    .RequireAuthorization();

await app.StartAsync();
var client = app.GetTestClient();

await ExpectStatusAsync(client, "/private", credential: null, HttpStatusCode.Unauthorized);
await ExpectStatusAsync(
    client,
    "/private",
    DemoTokenHandler.RejectedCredential,
    HttpStatusCode.Unauthorized);
await ExpectStatusAsync(
    client,
    "/private",
    DemoTokenHandler.AcceptedCredential,
    HttpStatusCode.OK);

metrics.Verify(scheme);

Console.WriteLine("PASS: authentication results include none, failure, and success.");
Console.WriteLine("PASS: missing and rejected credentials both returned 401 and produced two challenges.");
Console.WriteLine("PASS: attributes expose the exception type, not credentials or the exception message.");

static async Task ExpectStatusAsync(
    HttpClient client,
    string path,
    string? credential,
    HttpStatusCode expected)
{
    using var request = new HttpRequestMessage(HttpMethod.Get, path);
    if (credential is not null)
    {
        request.Headers.Add(DemoTokenHandler.HeaderName, credential);
    }

    using var response = await client.SendAsync(request);
    if (response.StatusCode != expected)
    {
        throw new InvalidOperationException(
            $"Expected {(int)expected} for {path}, got {(int)response.StatusCode}.");
    }
}

sealed class DemoTokenHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string HeaderName = "X-Demo-Credential";
    public const string AcceptedCredential = "accepted-demo-credential-7d3a";
    public const string RejectedCredential = "rejected-demo-credential-4c8b";
    public const string FailureMessage = "Rejected test credential.";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var credential = Request.Headers[HeaderName].ToString();

        if (string.IsNullOrEmpty(credential))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        if (!string.Equals(credential, AcceptedCredential, StringComparison.Ordinal))
        {
            return Task.FromResult(
                AuthenticateResult.Fail(
                    new InvalidOperationException(FailureMessage)));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.Name, "demo-user")],
            Scheme.Name);
        var ticket = new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name);
        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

sealed class AuthenticationMetricCollector : IDisposable
{
    private const string MeterName = "Microsoft.AspNetCore.Authentication";
    private const string DurationName = "aspnetcore.authentication.authenticate.duration";
    private const string ChallengeName = "aspnetcore.authentication.challenges";

    private readonly ConcurrentQueue<MetricPoint> _points = new();
    private readonly MeterListener _listener = new();

    public AuthenticationMetricCollector()
    {
        _listener.InstrumentPublished = (instrument, listener) =>
        {
            if (instrument.Meter.Name == MeterName)
            {
                listener.EnableMeasurementEvents(instrument);
            }
        };

        _listener.SetMeasurementEventCallback<double>(
            (instrument, measurement, tags, _) => Record(instrument, measurement, tags));
        _listener.SetMeasurementEventCallback<long>(
            (instrument, measurement, tags, _) => Record(instrument, measurement, tags));
        _listener.Start();
    }

    public void Verify(string expectedScheme)
    {
        var points = _points.ToArray();
        var durations = points.Where(point => point.Name == DurationName).ToArray();
        var results = durations
            .Select(point => point.Tags.GetValueOrDefault("aspnetcore.authentication.result"))
            .Where(result => result is not null)
            .ToHashSet(StringComparer.Ordinal);

        Require(results.SetEquals(["none", "failure", "success"]),
            $"Expected none, failure, and success; got: {string.Join(", ", results)}.");

        Require(
            durations.All(point =>
                point.Tags.GetValueOrDefault("aspnetcore.authentication.scheme") == expectedScheme),
            "All three captured duration measurements should carry the configured scheme.");

        var failedDuration = durations.Single(point =>
            point.Tags.GetValueOrDefault("aspnetcore.authentication.result") == "failure");
        Require(
            failedDuration.Tags.GetValueOrDefault("error.type")
                == typeof(InvalidOperationException).FullName,
            "The failed result should expose the exception type.");

        var challengeTotal = points
            .Where(point => point.Name == ChallengeName)
            .Sum(point => point.Value);
        Require(challengeTotal == 2, $"Expected two challenges; got {challengeTotal}.");

        var allowedAttributeNames = new HashSet<string>(StringComparer.Ordinal)
        {
            "aspnetcore.authentication.result",
            "aspnetcore.authentication.scheme",
            "error.type",
        };
        var unexpectedAttributeNames = points
            .SelectMany(point => point.Tags.Keys)
            .Where(name => !allowedAttributeNames.Contains(name))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Require(unexpectedAttributeNames.Length == 0,
            $"Unexpected metric attributes: {string.Join(", ", unexpectedAttributeNames)}.");

        var attributeValues = points
            .SelectMany(point => point.Tags.Values)
            .Where(value => value is not null)
            .ToArray();
        Require(!attributeValues.Any(value =>
                value!.Contains(DemoTokenHandler.AcceptedCredential, StringComparison.Ordinal)),
            "The accepted credential leaked into metric attributes.");
        Require(!attributeValues.Any(value =>
                value!.Contains(DemoTokenHandler.RejectedCredential, StringComparison.Ordinal)),
            "The rejected credential leaked into metric attributes.");
        Require(!attributeValues.Any(value =>
                value!.Contains(DemoTokenHandler.FailureMessage, StringComparison.Ordinal)),
            "The exception message leaked into metric attributes.");
    }

    private void Record<T>(
        Instrument instrument,
        T measurement,
        ReadOnlySpan<KeyValuePair<string, object?>> tags)
        where T : struct
    {
        var snapshot = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            snapshot[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture);
        }

        _points.Enqueue(new MetricPoint(
            instrument.Name,
            Convert.ToDouble(measurement, CultureInfo.InvariantCulture),
            snapshot));
    }

    private static void Require(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    public void Dispose() => _listener.Dispose();

    private sealed record MetricPoint(
        string Name,
        double Value,
        IReadOnlyDictionary<string, string?> Tags);
}
