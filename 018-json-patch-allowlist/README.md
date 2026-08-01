# ASP.NET Core 10 JSON Patch allowlist

This sample shows how to validate a `JsonPatchDocument<T>` before calling
`ApplyTo`. It rejects unexpected paths and operations, applies an accepted
patch to a separate DTO, and validates business rules before copying values
back to the domain object.

## Problem

JSON Patch can target any property exposed by its target type. Operations such
as `copy` can also amplify data. The ASP.NET Core implementation intentionally
leaves application-specific threat mitigation to the application.

The sample uses four boundaries:

1. A patch-specific DTO exposes only editable fields.
2. A guard limits the operation count, operation types, and exact paths.
3. `ApplyTo` runs against a candidate object, not the stored domain object.
4. Business invariants are checked before a new domain value is returned.

## Prerequisites

- .NET 10 SDK
- Network access for the first NuGet restore

No credentials or paid services are required.

## Setup and validation

From this folder, run:

```shell
dotnet restore tests/JsonPatchAllowlist.Tests/JsonPatchAllowlist.Tests.csproj
dotnet format src/JsonPatchAllowlist/JsonPatchAllowlist.csproj --verify-no-changes
dotnet format tests/JsonPatchAllowlist.Tests/JsonPatchAllowlist.Tests.csproj --verify-no-changes
dotnet build tests/JsonPatchAllowlist.Tests/JsonPatchAllowlist.Tests.csproj --configuration Release --no-restore
dotnet run --project tests/JsonPatchAllowlist.Tests/JsonPatchAllowlist.Tests.csproj --configuration Release --no-build
```

The final command runs six deterministic offline checks and prints:

```text
PASS allowed replacement preserves protected fields
PASS protected path is rejected
PASS copy operation is rejected
PASS operation limit is enforced
PASS failed test leaves the domain object unchanged
PASS invalid business value is rejected
6/6 checks passed
```

## Limitations

This is the validation seam, not a complete HTTP endpoint. A production API
must also authenticate and authorize the caller, cap the request-body size,
apply rate limits where appropriate, log rejected patches safely, and build a
threat model for its own object graph. The fixed path allowlist is intentional;
applications that expose nested objects or arrays need stricter JSON Pointer
and complexity rules.
