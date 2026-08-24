# ASP.NET Core 10 endpoint action metadata

This sample replaces the obsolete `IActionContextAccessor` pattern with
`IHttpContextAccessor`, `HttpContext.GetEndpoint()`, and endpoint metadata. It
also covers a subtle migration case: a selected endpoint is not necessarily an
MVC controller action, so `ActionDescriptor` can legitimately be absent.

## Problem

ASP.NET Core 10 marks `IActionContextAccessor` and `ActionContextAccessor`
obsolete with diagnostic `ASPDEPR006`. Services that used the accessor for the
current MVC action need another way to read action and custom routing metadata.

The `EndpointActionMetadataReader` reads the selected endpoint first. It then
looks up `ActionDescriptor`, narrows to `ControllerActionDescriptor` only when
that metadata exists, and reads an independent `AuditPolicyMetadata` value.

## Prerequisites

- A stable .NET 10 SDK. The sample was verified with SDK 10.0.303 and ASP.NET
  Core runtime 10.0.11.
- No credentials, account, database, external service, paid API, or runtime
  network access is required.

## Setup and run

From this folder:

```powershell
dotnet restore .\EndpointMetadataDemo.csproj
dotnet run --project .\EndpointMetadataDemo.csproj -c Release
```

The program creates `DefaultHttpContext` instances in memory. One contains MVC
controller metadata and one represents a non-MVC endpoint. No server is opened.

## Deterministic verification

Run the complete validation sequence:

```powershell
dotnet restore .\EndpointMetadataDemo.csproj
dotnet format .\EndpointMetadataDemo.csproj --verify-no-changes --no-restore
dotnet build .\EndpointMetadataDemo.csproj -c Release --no-restore
dotnet run --project .\EndpointMetadataDemo.csproj -c Release --no-build
dotnet list .\EndpointMetadataDemo.csproj package --include-transitive
dotnet list .\EndpointMetadataDemo.csproj package --vulnerable --include-transitive
```

Expected output ends with:

```text
[PASS] Non-MVC endpoint keeps endpoint metadata
[PASS] Cleared request returns no snapshot
Result: 6/6 checks passed.
```

The verifier checks the no-request case, the endpoint display name, MVC action
metadata, custom endpoint metadata, a non-MVC endpoint, and request cleanup. A
failure produces exit code 1.

## Limitations

This is an in-memory metadata contract test, not an end-to-end routing test. It
does not start Kestrel, execute MVC filters, perform model binding, or prove that
middleware ran after routing. `IHttpContextAccessor` should still be avoided in
code that can receive the required request data directly. If a background task
needs the values later, copy the small immutable values during the request
instead of retaining `HttpContext`.

There are no credential placeholders because this sample has no authenticated
dependency. Add secrets through your normal protected configuration provider if
you adapt the pattern to metadata that references an external service.
