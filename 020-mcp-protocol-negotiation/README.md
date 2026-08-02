# MCP protocol negotiation in C#

This sample verifies the protocol-negotiation behavior introduced by the stable MCP C# SDK 2.0 and the `2026-07-28` MCP specification.

It starts local Streamable HTTP servers in both stateless and stateful modes, then proves four behaviors:

1. A default client negotiates `2026-07-28` with a stateless server.
2. A default client falls back to an initialize-era protocol when the server requires a session.
3. A client pinned to `2026-07-28` connects to a stateless server.
4. The pinned client rejects a stateful server instead of silently downgrading.

## Prerequisites

- .NET SDK 9.0 or later
- Network access for the initial NuGet restore

No API key, model call, external MCP server, or paid service is required. Verification uses loopback HTTP only.

## Restore and verify

From this folder:

```shell
dotnet restore McpProtocolNegotiation.Verifier/McpProtocolNegotiation.Verifier.csproj
dotnet format McpProtocolNegotiation.Verifier/McpProtocolNegotiation.Verifier.csproj --verify-no-changes --no-restore
dotnet build McpProtocolNegotiation.Verifier/McpProtocolNegotiation.Verifier.csproj --configuration Release --no-restore
dotnet run --project McpProtocolNegotiation.Verifier/McpProtocolNegotiation.Verifier.csproj --configuration Release --no-build
```

The verifier exits with code `0` after printing four `PASS` lines. A failed assertion or unexpected connection result exits nonzero.

## What to use in production

Leave `McpClientOptions.ProtocolVersion` unset when compatibility with older servers is more important than the 2026 wire contract. Pin it to `2026-07-28` when the application requires sessionless transport behavior or a feature that cannot operate after fallback.

This sample tests negotiation only. Production servers also need authentication, authorization, host validation, restrictive CORS where applicable, request limits, observability, and a deliberate state-management design.
