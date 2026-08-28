using System.Net;
using System.Text;
using ClaudeFilesReconciliation;

var requestedIds = new[]
{
    "file_01ACTIVEALPHA000000000000",
    "file_02DELETEDBRAVO0000000000",
    "file_03ACTIVECHEETAH000000000",
};

const string fixture = """
    {
      "data": [
        { "id": "file_01ACTIVEALPHA000000000000" },
        { "id": "file_03ACTIVECHEETAH000000000" }
      ],
      "next_page": null
    }
    """;

using var handler = new FixtureHandler(fixture);
using var client = new HttpClient(handler)
{
    BaseAddress = new Uri("https://api.anthropic.com"),
};
client.DefaultRequestHeaders.Add("anthropic-version", "2023-06-01");
client.DefaultRequestHeaders.Add("x-api-key", "placeholder-not-used");

var result = await ClaudeFilesVerifier.ReconcileAsync(client, requestedIds);
var query = ParseQuery(handler.LastRequestUri?.Query);
var checks = 0;

Check(handler.RequestCount == 1 && handler.LastMethod == HttpMethod.Get,
    "one GET request was intercepted offline");
Check(handler.LastRequestUri?.AbsolutePath == "/v1/files",
    "the request targets /v1/files");
Check(query.GetValueOrDefault("ids[]", []).SequenceEqual(requestedIds) &&
      !query.ContainsKey("page") &&
      !query.ContainsKey("limit"),
    "repeated ids[] values are exact, with no page or limit");
Check(!handler.HadBetaHeader &&
      handler.AnthropicVersion == "2023-06-01" &&
      handler.ApiKey == "placeholder-not-used",
    "the stable request omits the Files beta header");
Check(result.Returned.SequenceEqual([requestedIds[0], requestedIds[2]]),
    "returned IDs preserve the fixture response order");
Check(result.Missing.SequenceEqual([requestedIds[1]]),
    "the silently omitted requested ID is detected");
Check(result.Unexpected.Count == 0 && result.Duplicates.Count == 0,
    "the response contains no unexpected or duplicate IDs");
Check(!result.IsComplete,
    "incomplete reconciliation blocks downstream work");

Console.WriteLine($"Returned: {string.Join(", ", result.Returned)}");
Console.WriteLine($"Missing:  {string.Join(", ", result.Missing)}");
Console.WriteLine($"Decision: {(result.IsComplete ? "CONTINUE" : "BLOCK")}");
Console.WriteLine($"Verifier: {checks}/8 passed");
return 0;

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {description}");
    }

    checks++;
    Console.WriteLine($"PASS {checks}: {description}");
}

static Dictionary<string, List<string>> ParseQuery(string? query)
{
    var values = new Dictionary<string, List<string>>(StringComparer.Ordinal);
    foreach (var pair in (query ?? string.Empty)
        .TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries))
    {
        var parts = pair.Split('=', 2);
        var key = Uri.UnescapeDataString(parts[0]);
        var value = parts.Length == 2 ? Uri.UnescapeDataString(parts[1]) : string.Empty;

        if (!values.TryGetValue(key, out var bucket))
        {
            bucket = [];
            values.Add(key, bucket);
        }

        bucket.Add(value);
    }

    return values;
}

internal sealed class FixtureHandler(string responseJson) : HttpMessageHandler
{
    public int RequestCount { get; private set; }

    public HttpMethod? LastMethod { get; private set; }

    public Uri? LastRequestUri { get; private set; }

    public bool HadBetaHeader { get; private set; }

    public string? AnthropicVersion { get; private set; }

    public string? ApiKey { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        RequestCount++;
        LastMethod = request.Method;
        LastRequestUri = request.RequestUri;
        HadBetaHeader = request.Headers.Contains("anthropic-beta");
        AnthropicVersion = request.Headers.TryGetValues("anthropic-version", out var versions)
            ? versions.Single()
            : null;
        ApiKey = request.Headers.TryGetValues("x-api-key", out var keys)
            ? keys.Single()
            : null;

        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(responseJson, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };

        return Task.FromResult(response);
    }
}
