# ASP.NET Core 10 JSON Patch guardrails

This sample shows how to validate an incoming `JsonPatchDocument<T>` before
`ApplyTo` can mutate application state. It uses the stable .NET 10
`Microsoft.AspNetCore.JsonPatch.SystemTextJson` package.

The guardrail:

- patches a narrow DTO instead of a tracked entity;
- allowlists paths and operation types;
- caps the number of operations;
- validates the patched candidate before creating a new domain value; and
- returns the untouched original when document application or domain validation fails.

## Prerequisites

- .NET 10 SDK (validated with SDK 10.0.302)
- Network access for the first NuGet restore only

No API key, database, or paid service is required.

## Restore, build, and test

From this folder, run:

```shell
dotnet restore JsonPatchGuardrails.slnx
dotnet build JsonPatchGuardrails.slnx --configuration Release --no-restore
dotnet test JsonPatchGuardrails.slnx --configuration Release --no-build
dotnet format JsonPatchGuardrails.slnx --verify-no-changes --no-restore
```

## Expected behavior

The seven deterministic tests prove that a safe `replace` succeeds, while the
following cases leave the original `Profile` untouched:

- a path such as `/isAdmin` is not on the allowlist;
- a `copy` operation is submitted;
- a document contains more than eight operations;
- a document contains a null operation;
- a `test` operation fails; or
- the patched result violates a business rule.

The time-zone allowlist is intentionally small so the final validation failure
is easy to reproduce. Replace it with domain rules appropriate to your API.

## Limitation

These checks reduce risk; they are not a complete JSON Patch threat model.
Production endpoints still need authentication, authorization, request-body
limits, rate limits, concurrency control, and application-specific validation.
