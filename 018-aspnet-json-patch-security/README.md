# ASP.NET Core JSON Patch security gate

This .NET 10 sample prevents a JSON Patch document from becoming an unrestricted write API. It validates the document before `ApplyTo`, patches a disposable input DTO, validates the resulting values, and only then maps allowed fields to the domain object.

The verifier covers six deterministic cases: a valid contact update, a sensitive path, a `copy` operation, too many operations, an `ApplyTo` type failure after an earlier valid operation, and a business-rule failure.

## Prerequisites

- .NET 10 SDK (the sample pins SDK `10.0.302` with feature-band roll-forward)
- Network access for the first NuGet restore

The source project pins the stable `Microsoft.AspNetCore.JsonPatch.SystemTextJson` package at `10.0.10`. No credentials or external services are used.

## Setup and verification

From this folder:

```shell
dotnet restore tests/JsonPatchSecurity.Verifier/JsonPatchSecurity.Verifier.csproj
dotnet format src/JsonPatchSecurity/JsonPatchSecurity.csproj --verify-no-changes --no-restore
dotnet format tests/JsonPatchSecurity.Verifier/JsonPatchSecurity.Verifier.csproj --verify-no-changes --no-restore
dotnet build tests/JsonPatchSecurity.Verifier/JsonPatchSecurity.Verifier.csproj --configuration Release --no-restore
dotnet run --project tests/JsonPatchSecurity.Verifier/JsonPatchSecurity.Verifier.csproj --configuration Release --no-build
```

Expected final output:

```text
PASS safe contact fields are updated
PASS security-sensitive paths are rejected
PASS copy operations are rejected
PASS oversized documents are rejected
PASS ApplyTo failures do not partially mutate the domain object
PASS business-rule failures do not mutate the domain object
6/6 checks passed.
```

## The policy boundary

`CustomerPatchInput` contains only client-editable properties. `CustomerPatchGuard` then applies four controls:

1. Reject more than five operations.
2. Accept only `replace` operations.
3. Accept only `/displayName` and `/email` paths.
4. Apply to a copy, validate the result, and map it to `Customer` only after every check succeeds.

An endpoint can call the guard before saving:

```csharp
var decision = guard.ValidateAndApply(customer, patchDocument);

if (!decision.Succeeded)
{
    return Results.ValidationProblem(
        new Dictionary<string, string[]> { ["patch"] = decision.Errors.ToArray() });
}

await database.SaveChangesAsync();
return Results.Ok(customer);
```

Protect the endpoint with authentication and resource-level authorization as well. The guard is a business-policy boundary, not an authentication mechanism.

## Expected behavior

A valid patch changes the customer's display name and email while `IsAdmin`, `CreditLimit`, and `Id` remain outside the patch model. Malicious or malformed documents return errors, and the original domain object stays unchanged.

## Limitations

This sample intentionally rejects `add`, `remove`, `move`, `copy`, and `test`; another API may need a different allowlist. It doesn't demonstrate persistence, concurrency tokens, request-body byte limits, rate limiting, or authorization. Collection paths require additional depth, index, and algorithmic-complexity controls. Treat the policy as a starting point for an application-specific threat model, not a universal JSON Patch firewall.

## Primary references

- [Microsoft: JSON Patch in ASP.NET Core web API](https://learn.microsoft.com/aspnet/core/web-api/jsonpatch?view=aspnetcore-10.0)
- [NuGet: Microsoft.AspNetCore.JsonPatch.SystemTextJson](https://www.nuget.org/packages/Microsoft.AspNetCore.JsonPatch.SystemTextJson/10.0.10)
- [IETF RFC 6902: JSON Patch](https://www.rfc-editor.org/rfc/rfc6902)
