using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace EndpointMetadataDemo;

public sealed class EndpointActionMetadataReader(IHttpContextAccessor httpContextAccessor)
{
    public EndpointActionSnapshot? Read()
    {
        var endpoint = httpContextAccessor.HttpContext?.GetEndpoint();
        if (endpoint is null)
        {
            return null;
        }

        var actionDescriptor = endpoint.Metadata.GetMetadata<ActionDescriptor>();
        var controllerDescriptor = actionDescriptor as ControllerActionDescriptor;
        var auditPolicy = endpoint.Metadata.GetMetadata<AuditPolicyMetadata>();

        return new EndpointActionSnapshot(
            endpoint.DisplayName,
            actionDescriptor?.DisplayName,
            controllerDescriptor?.ControllerName,
            controllerDescriptor?.ActionName,
            auditPolicy?.PolicyName);
    }
}

public sealed record EndpointActionSnapshot(
    string? EndpointDisplayName,
    string? ActionDisplayName,
    string? ControllerName,
    string? ActionName,
    string? AuditPolicy);

public sealed record AuditPolicyMetadata(string PolicyName);
