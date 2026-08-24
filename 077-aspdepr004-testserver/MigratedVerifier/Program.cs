using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using var host = await new HostBuilder()
    .ConfigureLogging(logging => logging.ClearProviders())
    .ConfigureWebHost(webHost =>
    {
        webHost
            .UseEnvironment("Testing")
            .UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddSingleton<IMessageSource>(
                    new FixedMessageSource("ready"));
            })
            .Configure(app =>
            {
                app.Run(async context =>
                {
                    var source = context.RequestServices
                        .GetRequiredService<IMessageSource>();
                    var environment = context.RequestServices
                        .GetRequiredService<IWebHostEnvironment>();

                    context.Response.Headers["X-Test-Host"] = "generic";
                    await context.Response.WriteAsJsonAsync(new ProbeResponse(
                        source.Message,
                        environment.EnvironmentName,
                        context.Request.Path.Value));
                });
            });
    })
    .StartAsync();

try
{
    var passed = 0;

    Check(host.GetTestServer() is not null, "TestServer is registered");
    passed++;

    using var client = host.GetTestClient();
    using var response = await client.GetAsync("/probe");

    Check(response.StatusCode is HttpStatusCode.OK, "response status is 200");
    passed++;
    Check(
        response.Headers.GetValues("X-Test-Host").Single() == "generic",
        "response came from the generic-host pipeline");
    passed++;
    Check(
        response.Content.Headers.ContentType?.MediaType == "application/json",
        "response content type is JSON");
    passed++;

    var body = await response.Content.ReadFromJsonAsync<ProbeResponse>();
    Check(body is not null, "response body deserializes");
    passed++;
    Check(body!.Message == "ready", "test service is resolved");
    passed++;
    Check(body.Environment == "Testing", "test environment is retained");
    passed++;
    Check(body.Path == "/probe", "request path is retained");
    passed++;

    Console.WriteLine($"{passed}/8 migration checks passed");
}
finally
{
    await host.StopAsync();
}

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {name}");
    }

    Console.WriteLine($"PASS: {name}");
}

internal interface IMessageSource
{
    string Message { get; }
}

internal sealed record FixedMessageSource(string Message) : IMessageSource;

internal sealed record ProbeResponse(
    string Message,
    string Environment,
    string? Path);
