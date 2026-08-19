# .NET 10 strict JSON input

ASP.NET Core's web JSON defaults accept duplicate properties, ignore unmapped properties, and bind names without regard to case. Those defaults are useful for compatibility, but they can also let an ambiguous request reach business logic.

This sample keeps ASP.NET Core's web-oriented camel-case configuration while enabling the five checks represented by `JsonSerializerOptions.Strict`. It proves that duplicate, unknown, null, incomplete, and incorrectly cased payloads are rejected before the endpoint handler runs.

## Prerequisites

- .NET 10 SDK with the .NET 10.0.11 runtime or a later supported .NET 10 servicing release
- Network access for the initial NuGet restore only
- No API key or other credential

## Setup

From this folder, restore the pinned test-host dependency:

```powershell
dotnet restore StrictJsonInput.csproj --nologo
```

## Run and verify

Format-check, build, and run the deterministic verifier:

```powershell
dotnet format StrictJsonInput.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build StrictJsonInput.csproj --configuration Release --no-restore --nologo
dotnet run --project StrictJsonInput.csproj --configuration Release --no-build
```

The executable first shows how `JsonSerializerOptions.Web` resolves a duplicate `role` to the final value while ignoring `isAdmin`. It then hosts the Minimal API in memory with `TestServer` and checks this matrix:

| Case | Expected status |
|---|---:|
| Valid camel-case payload | 200 |
| Duplicate `role` | 400 |
| Unknown `isAdmin` | 400 |
| `null` non-nullable `displayName` | 400 |
| Missing required `role` constructor argument | 400 |
| Incorrectly cased `DisplayName` | 400 |

The final line should be:

```text
VERIFIED 6/6 HTTP cases; handler calls=1
```

No external service is contacted during execution, and no paid request is made.

## How it works

`ConfigureHttpJsonOptions` mutates the existing ASP.NET Core web options, so the app retains its camel-case naming policy. The sample then enables duplicate rejection, unmapped-member rejection, case-sensitive binding, nullable-annotation enforcement, and required-constructor-parameter enforcement.

The verifier counts handler calls as well as status codes. A rejection is only successful when the invalid body returns 400 and never crosses the endpoint boundary.

## Limitations

Strict input is a compatibility decision. Existing clients that send extra fields or use inconsistent casing will fail after this change, so roll it out with contract tests and client communication.

`RespectNullableAnnotations` doesn't validate top-level types, collection elements, or generic members. It also doesn't make ordinary non-constructor properties required; use `required`, `JsonRequired`, or contract metadata for that rule. Serializer checks don't replace domain validation. Controller-based APIs use MVC JSON options rather than the Minimal API options configured in this sample.
