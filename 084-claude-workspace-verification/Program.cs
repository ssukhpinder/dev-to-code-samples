using System.Net;

const string ExpectedWorkspace = "wrkspc_01ExpectedWorkspace";
const string MessageJson = """
    {"model":"claude-example","max_tokens":16,"messages":[{"role":"user","content":"fixture"}]}
    """;

var checks = new (string Name, Func<Task> Run)[]
{
    ("matching workspace passes and request is scoped", MatchingWorkspacePassesAsync),
    ("stale routing configuration makes no request", StaleRoutingMakesNoRequestAsync),
    ("wrong workspace fails before body use", WrongWorkspaceFailsAsync),
    ("missing workspace on success fails closed", MissingWorkspaceFailsAsync),
    ("malformed response workspace fails closed", MalformedWorkspaceFailsAsync),
    ("invalid configured workspace makes no request", InvalidConfigurationMakesNoRequestAsync),
    ("authentication failure remains the primary error", AuthenticationFailureRemainsPrimaryAsync)
};

var passed = 0;
foreach (var check in checks)
{
    await check.Run();
    Console.WriteLine($"PASS {check.Name}");
    passed++;
}

Console.WriteLine($"{passed} deterministic checks passed.");

static async Task MatchingWorkspacePassesAsync()
{
    var handler = new FixtureHttpMessageHandler(request =>
    {
        Assert(
            request.Headers.GetValues("anthropic-workspace-id").Single() == ExpectedWorkspace,
            "The request did not carry the configured workspace ID.");

        return FixtureHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            "{\"id\":\"msg_fixture\"}",
            ExpectedWorkspace);
    });

    using var httpClient = CreateHttpClient(handler);
    var body = await new WorkspaceVerifiedClient(httpClient)
        .PostMessageAsync(ExpectedWorkspace, ExpectedWorkspace, MessageJson);

    Assert(body == "{\"id\":\"msg_fixture\"}", "Verified response body changed.");
    Assert(handler.CallCount == 1, "Expected exactly one request.");
}

static async Task StaleRoutingMakesNoRequestAsync()
{
    var handler = new FixtureHttpMessageHandler(_ =>
        throw new InvalidOperationException("The request should not have been sent."));
    using var httpClient = CreateHttpClient(handler);

    await AssertThrowsAsync<InvalidDataException>(
        () => new WorkspaceVerifiedClient(httpClient).PostMessageAsync(
            "wrkspc_01StaleDeploymentValue",
            ExpectedWorkspace,
            MessageJson),
        "independently authorized workspace");

    Assert(handler.CallCount == 0, "Stale routing configuration reached the transport.");
}

static async Task WrongWorkspaceFailsAsync()
{
    using var httpClient = CreateHttpClient(new FixtureHttpMessageHandler(_ =>
        FixtureHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            "not-json-on-purpose",
            "wrkspc_01UnexpectedWorkspace")));

    await AssertThrowsAsync<InvalidDataException>(
        () => new WorkspaceVerifiedClient(httpClient)
            .PostMessageAsync(ExpectedWorkspace, ExpectedWorkspace, MessageJson),
        "resolved workspace");
}

static async Task MissingWorkspaceFailsAsync()
{
    using var httpClient = CreateHttpClient(new FixtureHttpMessageHandler(_ =>
        FixtureHttpMessageHandler.JsonResponse(HttpStatusCode.OK, "{}")));

    await AssertThrowsAsync<InvalidDataException>(
        () => new WorkspaceVerifiedClient(httpClient)
            .PostMessageAsync(ExpectedWorkspace, ExpectedWorkspace, MessageJson),
        "omitted anthropic-workspace-id");
}

static async Task MalformedWorkspaceFailsAsync()
{
    using var httpClient = CreateHttpClient(new FixtureHttpMessageHandler(_ =>
        FixtureHttpMessageHandler.JsonResponse(
            HttpStatusCode.OK,
            "{}",
            "workspace with spaces")));

    await AssertThrowsAsync<InvalidDataException>(
        () => new WorkspaceVerifiedClient(httpClient)
            .PostMessageAsync(ExpectedWorkspace, ExpectedWorkspace, MessageJson),
        "response workspace ID");
}

static async Task InvalidConfigurationMakesNoRequestAsync()
{
    var handler = new FixtureHttpMessageHandler(_ =>
        throw new InvalidOperationException("The request should not have been sent."));
    using var httpClient = CreateHttpClient(handler);

    await AssertThrowsAsync<InvalidDataException>(
        () => new WorkspaceVerifiedClient(httpClient)
            .PostMessageAsync("not-a-workspace", ExpectedWorkspace, MessageJson),
        "target workspace ID");

    Assert(handler.CallCount == 0, "Invalid configuration reached the transport.");
}

static async Task AuthenticationFailureRemainsPrimaryAsync()
{
    using var httpClient = CreateHttpClient(new FixtureHttpMessageHandler(_ =>
        FixtureHttpMessageHandler.JsonResponse(HttpStatusCode.Unauthorized, "{}")));

    try
    {
        await new WorkspaceVerifiedClient(httpClient)
            .PostMessageAsync(ExpectedWorkspace, ExpectedWorkspace, MessageJson);
        throw new InvalidOperationException("Expected an HTTP authentication failure.");
    }
    catch (HttpRequestException exception) when (exception.StatusCode == HttpStatusCode.Unauthorized)
    {
        // Expected: a 401 may not carry anthropic-workspace-id.
    }
}

static HttpClient CreateHttpClient(HttpMessageHandler handler) => new(handler)
{
    BaseAddress = new Uri("https://api.anthropic.com/")
};

static async Task AssertThrowsAsync<TException>(Func<Task> action, string expectedMessage)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException exception) when (
        exception.Message.Contains(expectedMessage, StringComparison.Ordinal))
    {
        return;
    }

    throw new InvalidOperationException(
        $"Expected {typeof(TException).Name} containing '{expectedMessage}'.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
