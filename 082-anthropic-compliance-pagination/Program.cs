using System.Net;
using ComplianceTranscriptPagination;

var checks = new List<(string Name, Func<Task> Run)>
{
    ("a short page follows next_page until null", VerifyShortPageContinuesAsync),
    ("the returned message order is preserved", VerifyReturnedOrderAsync),
    ("a repeated cursor fails closed", VerifyRepeatedCursorFailsAsync),
    ("an invalid cursor response remains visible", VerifyBadRequestIsSurfacedAsync),
};

foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine($"PASS {check.Name}");
}

Console.WriteLine($"{checks.Count} deterministic checks passed.");

static async Task VerifyShortPageContinuesAsync()
{
    using var handler = CreateHappyPathHandler();
    using var client = CreateClient(handler);
    var pager = new ComplianceTranscriptPager(client);

    var messages = await pager.ReadAllAsync("clls_fixture", limit: 3);

    Equal(3, messages.Count, "all transcript messages should be returned");
    Equal(2, handler.Requests.Count, "the short first page must not terminate the walk");
    DoesNotContain("page=", handler.Requests[0].Query, "the first request must not send a cursor");
    Contains("page=page_fixture_2", handler.Requests[1].Query, "the next cursor must be sent");
    Contains("order=asc", handler.Requests[1].Query, "the walk must request oldest-first order");
}

static async Task VerifyReturnedOrderAsync()
{
    using var handler = CreateHappyPathHandler();
    using var client = CreateClient(handler);
    var pager = new ComplianceTranscriptPager(client);

    var messages = await pager.ReadAllAsync("clls_fixture", limit: 3);

    Equal(
        "msg_1,msg_2,msg_3",
        string.Join(',', messages.Select(message => message.Id)),
        "messages must not be re-sorted by timestamp");
    Equal(
        messages[0].CreatedAt,
        messages[1].CreatedAt,
        "the fixture must contain messages from one inference call with a shared timestamp");
}

static async Task VerifyRepeatedCursorFailsAsync()
{
    using var handler = new FixtureHttpMessageHandler((_, _) =>
        FixtureHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """
            {
              "data": [],
              "next_page": "page_repeated"
            }
            """));
    using var client = CreateClient(handler);
    var pager = new ComplianceTranscriptPager(client);

    await ThrowsAsync<InvalidDataException>(
        () => pager.ReadAllAsync("clls_fixture"),
        "a repeated next_page cursor must not create an infinite loop");
    Equal(2, handler.Requests.Count, "the repeated cursor should be rejected on its second appearance");
}

static async Task VerifyBadRequestIsSurfacedAsync()
{
    using var handler = new FixtureHttpMessageHandler((_, _) =>
        FixtureHttpMessageHandler.Json(
            HttpStatusCode.BadRequest,
            """
            {
              "type": "error",
              "error": { "type": "invalid_request_error", "message": "Invalid page cursor" }
            }
            """));
    using var client = CreateClient(handler);
    var pager = new ComplianceTranscriptPager(client);

    var exception = await ThrowsAsync<HttpRequestException>(
        () => pager.ReadAllAsync("clls_fixture"),
        "a 400 response must be surfaced so the caller can restart the walk");
    Equal(HttpStatusCode.BadRequest, exception.StatusCode, "the HTTP status should be retained");
}

static FixtureHttpMessageHandler CreateHappyPathHandler() =>
    new((_, requestNumber) => requestNumber switch
    {
        1 => FixtureHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """
            {
              "data": [
                { "id": "msg_1", "role": "user", "created_at": "2026-08-28T12:00:02Z" },
                { "id": "msg_2", "role": "assistant", "created_at": "2026-08-28T12:00:02Z" }
              ],
              "next_page": "page_fixture_2"
            }
            """),
        2 => FixtureHttpMessageHandler.Json(
            HttpStatusCode.OK,
            """
            {
              "data": [
                { "id": "msg_3", "role": "assistant", "created_at": "2026-08-28T12:00:03Z" }
              ],
              "next_page": null
            }
            """),
        _ => throw new InvalidOperationException("The pager requested a page after next_page was null."),
    });

static HttpClient CreateClient(HttpMessageHandler handler) =>
    new(handler, disposeHandler: false)
    {
        BaseAddress = new Uri("https://api.anthropic.test/"),
    };

static void Equal<T>(T expected, T actual, string message)
{
    if (!EqualityComparer<T>.Default.Equals(expected, actual))
    {
        throw new InvalidOperationException($"{message}. Expected: {expected}; actual: {actual}.");
    }
}

static void Contains(string expected, string actual, string message)
{
    if (!actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message}. Missing: {expected}.");
    }
}

static void DoesNotContain(string expected, string actual, string message)
{
    if (actual.Contains(expected, StringComparison.Ordinal))
    {
        throw new InvalidOperationException($"{message}. Unexpected: {expected}.");
    }
}

static async Task<TException> ThrowsAsync<TException>(Func<Task> action, string message)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception)
    {
        return exception;
    }

    throw new InvalidOperationException($"{message}. Expected {typeof(TException).Name}.");
}
