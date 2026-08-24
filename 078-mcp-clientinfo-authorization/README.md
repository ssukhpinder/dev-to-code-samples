# MCP `clientInfo` authorization

## Problem

MCP 2026-07-28 clients can send `io.modelcontextprotocol/clientInfo` with a name and version on every request. That metadata is self-reported. A caller can copy a familiar name, so a server must not treat it as proof of identity or use it to grant a privileged tool.

This sample keeps `clientInfo` out of the authorization API. An offline verifier proves that access follows claims on the identity selected from the configured authentication type and the case-sensitive `tools.admin` scope, regardless of the reported client name.

## Prerequisites

- .NET 10 SDK
- No MCP server, account, API, database, or credential is required

If the policy is adapted to an HTTP server, validate the real credential upstream and pass its authenticated principal into the authorizer. Use a placeholder such as `Authorization: Bearer <ACCESS_TOKEN>` in local documentation; never commit a token.

## Setup

From this folder, restore the project:

```powershell
dotnet restore McpClientInfoAuthorization.csproj --nologo
```

## Run

```powershell
dotnet run --project McpClientInfoAuthorization.csproj --configuration Release
```

Expected final output:

```text
10/10 checks passed
```

The process exits nonzero on the first failed assertion.

## Deterministic verification

Run the same gates used for the sample:

```powershell
dotnet restore McpClientInfoAuthorization.csproj --nologo
dotnet format McpClientInfoAuthorization.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build McpClientInfoAuthorization.csproj --configuration Release --no-restore --nologo
dotnet run --project McpClientInfoAuthorization.csproj --configuration Release --no-build
dotnet list McpClientInfoAuthorization.csproj package --include-transitive
dotnet list McpClientInfoAuthorization.csproj package --vulnerable --include-transitive
```

The ten fixtures verify that:

- anonymous and low-scope callers remain denied even when they report a trusted name;
- an authenticated identity with `tools.admin` remains allowed when the name is unfamiliar, changed, or absent;
- scope values are split only on the OAuth space delimiter and compared with ordinal case sensitivity; and
- a privileged claim on an unauthenticated identity or the wrong authentication type cannot be mixed into the decision.

The verifier uses fixed inputs and has no clock, randomness, locale, file fixture, or runtime network dependency.

## Expected behavior

`ToolAuthorizer.Authorize` accepts a `ClaimsPrincipal`, the configured authentication type, and the required scope. It has no `ClientInfo` parameter, which makes the trust boundary visible in the method signature. It selects exactly one authenticated identity from that type before reading any scope claim.

The sample treats OAuth scope values as ASCII-space-delimited, case-sensitive strings and validates the configured value as one `scope-token`. `tools.admin` is a sample-defined scope. In production, the resource server identifies the access needed for an operation, the authorization server grants scopes, and application policy evaluates the validated token data in the claim shape produced by the configured middleware.

## Limitations

This is a focused authorization-policy fixture, not an MCP transport, token validator, or complete OAuth resource server. It does not prove a client application's identity, parse an MCP request, or validate a bearer token. Production code should validate the token through the configured resource-server mechanism, including expiration and required audience or resource binding, and validate signature and issuer when applicable. Then select the intended authentication scheme through the platform's authorization policy before evaluating claims.

`clientInfo` is still useful for display, logging, compatibility diagnostics, and support. Treat every value as untrusted input, encode it for the actual output sink, and never let a familiar name bypass authenticated policy.
