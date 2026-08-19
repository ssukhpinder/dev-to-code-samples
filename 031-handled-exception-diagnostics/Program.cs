using System.Collections.Concurrent;
using System.Net;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

var logStore = new InMemoryLogStore();
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    EnvironmentName = Environments.Production
});

builder.Logging.ClearProviders();
builder.Logging.SetMinimumLevel(LogLevel.Trace);
builder.Logging.AddProvider(new InMemoryLoggerProvider(logStore));
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ApiExceptionHandler>();

var app = builder.Build();

app.UseExceptionHandler(new ExceptionHandlerOptions
{
    // true means suppress the middleware diagnostics; false means emit them.
    SuppressDiagnosticsCallback = context =>
        context.Exception is ExpectedOrderConflictException
});

app.MapGet("/expected", ThrowExpectedConflict);
app.MapGet("/dependency", ThrowDependencyFailure);

await app.StartAsync();

try
{
    var server = app.Services.GetRequiredService<IServer>();
    var addresses = server.Features.Get<IServerAddressesFeature>()?.Addresses
        ?? throw new InvalidOperationException("Kestrel did not expose its bound address.");
    var address = addresses.Single();

    using var client = new HttpClient { BaseAddress = new Uri(address) };
    logStore.Clear();

    await VerifyRequestAsync(
        client,
        logStore,
        "/expected",
        HttpStatusCode.Conflict,
        expectedMiddlewareLogs: 0);

    await VerifyRequestAsync(
        client,
        logStore,
        "/dependency",
        HttpStatusCode.ServiceUnavailable,
        expectedMiddlewareLogs: 1);

    Console.WriteLine("PASS: the handler produced safe responses for both failures.");
    Console.WriteLine("PASS: expected conflict suppressed built-in exception diagnostics.");
    Console.WriteLine("PASS: dependency failure retained built-in exception diagnostics.");
}
finally
{
    await app.StopAsync();
    await app.DisposeAsync();
}

static IResult ThrowExpectedConflict() =>
    throw new ExpectedOrderConflictException("The order was already completed.");

static IResult ThrowDependencyFailure() =>
    throw new DependencyUnavailableException("Inventory database unavailable.");

static async Task VerifyRequestAsync(
    HttpClient client,
    InMemoryLogStore logStore,
    string path,
    HttpStatusCode expectedStatus,
    int expectedMiddlewareLogs)
{
    logStore.Clear();

    using var response = await client.GetAsync(path);
    _ = await response.Content.ReadAsStringAsync();

    Ensure(
        response.StatusCode == expectedStatus,
        $"{path} returned {(int)response.StatusCode}, expected {(int)expectedStatus}.");

    var middlewareLogs = logStore.Snapshot()
        .Where(entry =>
            entry.Category.StartsWith(
                "Microsoft.AspNetCore.Diagnostics.ExceptionHandlerMiddleware",
                StringComparison.Ordinal)
            && entry.EventId.Name == "UnhandledException")
        .ToArray();

    Ensure(
        middlewareLogs.Length == expectedMiddlewareLogs,
        $"{path} emitted {middlewareLogs.Length} built-in UnhandledException logs; "
        + $"expected {expectedMiddlewareLogs}.");
}

static void Ensure(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}

sealed class ApiExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            ExpectedOrderConflictException =>
                (StatusCodes.Status409Conflict, "Order conflict"),
            _ =>
                (StatusCodes.Status503ServiceUnavailable, "Service unavailable")
        };

        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(
            new { status, title, traceId = httpContext.TraceIdentifier },
            cancellationToken);

        return true;
    }
}

sealed class ExpectedOrderConflictException(string message) : Exception(message);

sealed class DependencyUnavailableException(string message) : Exception(message);

sealed record CapturedLog(
    string Category,
    LogLevel Level,
    EventId EventId,
    Exception? Exception,
    string Message);

sealed class InMemoryLogStore
{
    private readonly ConcurrentQueue<CapturedLog> _entries = new();

    public void Add(CapturedLog entry) => _entries.Enqueue(entry);

    public CapturedLog[] Snapshot() => _entries.ToArray();

    public void Clear()
    {
        while (_entries.TryDequeue(out _))
        {
        }
    }
}

sealed class InMemoryLoggerProvider(InMemoryLogStore store) : ILoggerProvider
{
    public ILogger CreateLogger(string categoryName) =>
        new InMemoryLogger(categoryName, store);

    public void Dispose()
    {
    }
}

sealed class InMemoryLogger(string category, InMemoryLogStore store) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        store.Add(new CapturedLog(
            category,
            logLevel,
            eventId,
            exception,
            formatter(state, exception)));
    }
}

sealed class NullScope : IDisposable
{
    public static NullScope Instance { get; } = new();

    public void Dispose()
    {
    }
}
