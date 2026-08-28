using System.Net;
using System.Text;

namespace ComplianceTranscriptPagination;

internal sealed class FixtureHttpMessageHandler(
    Func<HttpRequestMessage, int, HttpResponseMessage> responseFactory) : HttpMessageHandler
{
    public List<Uri> Requests { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var requestUri = request.RequestUri
            ?? throw new InvalidOperationException("The request URI was missing.");

        Requests.Add(requestUri);
        return Task.FromResult(responseFactory(request, Requests.Count));
    }

    public static HttpResponseMessage Json(HttpStatusCode statusCode, string json) =>
        new(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
}
