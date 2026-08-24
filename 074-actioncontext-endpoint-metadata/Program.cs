using EndpointMetadataDemo;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Patterns;
using Microsoft.Extensions.DependencyInjection;

var services = new ServiceCollection();
services.AddHttpContextAccessor();
services.AddSingleton<EndpointActionMetadataReader>();

await using var provider = services.BuildServiceProvider();
var accessor = provider.GetRequiredService<IHttpContextAccessor>();
var reader = provider.GetRequiredService<EndpointActionMetadataReader>();
var results = new List<(string Name, bool Passed)>();

results.Add(("No active request returns no snapshot", reader.Read() is null));

var controllerDescriptor = new ControllerActionDescriptor
{
    DisplayName = "OrdersController.Get (EndpointMetadataDemo)",
    ControllerName = "Orders",
    ActionName = "Get",
};

accessor.HttpContext = CreateContext(
    "GET /orders/{id}",
    RoutePatternFactory.Parse("/orders/{id}"),
    controllerDescriptor,
    new AuditPolicyMetadata("orders.read"));

var controllerSnapshot = reader.Read();
results.Add((
    "Endpoint display name is available",
    controllerSnapshot?.EndpointDisplayName == "GET /orders/{id}"));
results.Add((
    "MVC action metadata is available",
    controllerSnapshot is
    {
        ActionDisplayName: "OrdersController.Get (EndpointMetadataDemo)",
        ControllerName: "Orders",
        ActionName: "Get",
    }));
results.Add((
    "Custom endpoint metadata is available",
    controllerSnapshot?.AuditPolicy == "orders.read"));

accessor.HttpContext = CreateContext(
    "GET /health",
    RoutePatternFactory.Parse("/health"),
    new AuditPolicyMetadata("health.read"));

var minimalSnapshot = reader.Read();
results.Add((
    "Non-MVC endpoint keeps endpoint metadata",
    minimalSnapshot is
    {
        EndpointDisplayName: "GET /health",
        ActionDisplayName: null,
        ControllerName: null,
        ActionName: null,
        AuditPolicy: "health.read",
    }));

accessor.HttpContext = null;
results.Add(("Cleared request returns no snapshot", reader.Read() is null));

foreach (var result in results)
{
    Console.WriteLine($"[{(result.Passed ? "PASS" : "FAIL")}] {result.Name}");
}

var passed = results.Count(result => result.Passed);
Console.WriteLine($"Result: {passed}/{results.Count} checks passed.");

return passed == results.Count ? 0 : 1;

static DefaultHttpContext CreateContext(
    string displayName,
    RoutePattern routePattern,
    params object[] metadata)
{
    var endpoint = new RouteEndpoint(
        static _ => Task.CompletedTask,
        routePattern,
        order: 0,
        new EndpointMetadataCollection(metadata),
        displayName);

    var context = new DefaultHttpContext();
    context.SetEndpoint(endpoint);
    return context;
}
