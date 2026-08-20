using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

var options = new ForwardedHeadersOptions
{
    ForwardedHeaders =
        ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto,
    ForwardLimit = 1
};

options.KnownIPNetworks.Clear();
options.KnownProxies.Clear();
options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse("10.20.0.0/16"));

var middleware = new ForwardedHeadersMiddleware(
    _ => Task.CompletedTask,
    NullLoggerFactory.Instance,
    Options.Create(options));

var trusted = await ForwardAsync(
    middleware,
    proxyAddress: "10.20.4.7",
    clientAddress: "203.0.113.42",
    forwardedScheme: "https");

var untrusted = await ForwardAsync(
    middleware,
    proxyAddress: "10.21.4.7",
    clientAddress: "203.0.113.42",
    forwardedScheme: "https");

var checks = new (string Name, bool Passed)[]
{
    ("trusted subnet updates RemoteIpAddress", trusted.RemoteIpAddress == "203.0.113.42"),
    ("trusted subnet updates the request scheme", trusted.Scheme == "https"),
    ("trusted subnet consumes X-Forwarded-For", trusted.ForwardedFor.Length == 0),
    ("trusted subnet records the original proxy", trusted.OriginalFor == "10.20.4.7:443"),
    ("adjacent subnet leaves RemoteIpAddress unchanged", untrusted.RemoteIpAddress == "10.21.4.7"),
    ("adjacent subnet leaves the request scheme unchanged", untrusted.Scheme == "http"),
    ("adjacent subnet keeps X-Forwarded-For", untrusted.ForwardedFor == "203.0.113.42"),
    ("adjacent subnet does not create X-Original-For", untrusted.OriginalFor.Length == 0)
};

foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
}

var passed = checks.Count(check => check.Passed);
Console.WriteLine($"{passed}/{checks.Length} checks passed.");

return passed == checks.Length ? 0 : 1;

static async Task<RequestSnapshot> ForwardAsync(
    ForwardedHeadersMiddleware middleware,
    string proxyAddress,
    string clientAddress,
    string forwardedScheme)
{
    var context = new DefaultHttpContext();
    context.Connection.RemoteIpAddress = IPAddress.Parse(proxyAddress);
    context.Connection.RemotePort = 443;
    context.Request.Scheme = "http";
    context.Request.Headers["X-Forwarded-For"] = clientAddress;
    context.Request.Headers["X-Forwarded-Proto"] = forwardedScheme;

    await middleware.Invoke(context);

    return new RequestSnapshot(
        context.Connection.RemoteIpAddress?.ToString() ?? "",
        context.Request.Scheme,
        context.Request.Headers["X-Forwarded-For"].ToString(),
        context.Request.Headers["X-Original-For"].ToString());
}

internal sealed record RequestSnapshot(
    string RemoteIpAddress,
    string Scheme,
    string ForwardedFor,
    string OriginalFor);
