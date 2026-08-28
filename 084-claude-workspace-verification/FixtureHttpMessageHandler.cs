using System.Net;

internal sealed class FixtureHttpMessageHandler(
    Func<HttpRequestMessage, HttpResponseMessage> responder) : HttpMessageHandler
{
    public int CallCount { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        CallCount++;
        return Task.FromResult(responder(request));
    }

    public static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string body,
        string? workspaceId = null)
    {
        var response = new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(body)
        };

        if (workspaceId is not null)
        {
            response.Headers.Add("anthropic-workspace-id", workspaceId);
        }

        return response;
    }
}
