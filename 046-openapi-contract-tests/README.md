# ASP.NET Core 10 OpenAPI contract tests

## Problem

An API can still compile when an endpoint path, operation ID, required parameter, request body, or documented response changes. Fetching `/openapi/v1.json` in a test catches that drift, but it also adds an HTTP round-trip to a check that only needs the generated document.

This sample starts ASP.NET Core on the in-memory `TestServer`, resolves the stable .NET 10 `IOpenApiDocumentProvider` service from dependency injection, and verifies a small OpenAPI 3.1 contract without Kestrel, a port, or an HTTP request.

## Prerequisites

- .NET 10 SDK
- `Microsoft.AspNetCore.OpenApi` and `Microsoft.AspNetCore.TestHost` 10.0.11, restored from NuGet
- No credentials, network service, environment variable, database, or secret placeholder is required

Microsoft documents programmatic document access in [Use generated OpenAPI documents](https://learn.microsoft.com/aspnet/core/fundamentals/openapi/using-openapi-documents?view=aspnetcore-10.0#support-for-injecting-iopenapidocumentprovider). The [ASP.NET Core 10 release notes](https://learn.microsoft.com/aspnet/core/release-notes/aspnetcore-10.0?view=aspnetcore-10.0#support-for-iopenapidocumentprovider-in-the-di-container) identify dependency-injected `IOpenApiDocumentProvider` as a .NET 10 feature.

## Setup and verification

From this folder, run:

```powershell
dotnet restore
dotnet format OpenApiContractTests.csproj --verify-no-changes --no-restore
dotnet build OpenApiContractTests.csproj -c Release --no-restore
dotnet run --project OpenApiContractTests.csproj -c Release --no-build
dotnet list OpenApiContractTests.csproj package --include-transitive
dotnet list OpenApiContractTests.csproj package --vulnerable --include-transitive
```

The console program is the deterministic verifier. A successful run prints:

```text
PASS document uses OpenAPI 3.1
PASS only the expected paths are present
PASS GET operation ID is stable
PASS GET order ID remains a required path parameter
PASS GET advertises 200 and 404 responses
PASS POST operation ID is stable
PASS POST request body remains required
PASS POST advertises 201 and 400 responses
8/8 checks passed.
```

The sample starts only the in-memory test host. It does not bind a port, start Kestrel, make an HTTP request, read the clock, use randomness, or make a runtime network call. Package restore is the only setup step that can require network access.

## What the contract checks protect

`AddOpenApi("contract")` registers a keyed provider for that document name. Starting `TestServer` finalizes the Minimal API descriptions without opening a socket. The sample then resolves the same key and calls `GetOpenApiDocumentAsync`. Serializing the result as OpenAPI 3.1 lets the verifier assert the consumer-facing JSON shape instead of depending on OpenAPI.NET implementation types.

The checks intentionally cover a narrow boundary: expected paths, stable operation IDs, a required route value and request body, and documented success and error responses. Add assertions for the fields your client generator, gateway, or integration actually depends on. Avoid snapshotting the whole document unless every metadata and ordering change should fail the build.

## Limitations

This verifies generated documentation, not runtime routing, authorization, model binding, serialization, or middleware behavior. Keep a smaller set of HTTP integration tests for those boundaries. A document generated outside an HTTP request can also lack deployment-specific server information. The sample uses the OpenAPI.NET version supplied transitively by `Microsoft.AspNetCore.OpenApi`; upgrading that dependency independently can introduce incompatible object-model changes.
