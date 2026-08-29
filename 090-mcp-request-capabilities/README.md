# MCP C# per-request client capabilities

MCP `2026-07-28` removes the initialization handshake from the modern stateless path. Protocol version and client capabilities now arrive in each request's reserved `_meta` fields, while client identity may also be supplied there. A server must not reuse the capability value from an earlier call.

The stable MCP C# SDK makes the distinction explicit. In stateless HTTP handlers, `request.Server.ClientCapabilities` is `null`; the authoritative value is `request.JsonRpcRequest.Context?.ClientCapabilities`. This sample sends two overlapping requests with different extension declarations and proves that each handler sees only its own metadata.

The behavior is documented in the official [MCP 2026-07-28 release](https://blog.modelcontextprotocol.io/posts/2026-07-28/), [C# SDK `JsonRpcMessageContext` reference](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Protocol.JsonRpcMessageContext.html), and [C# SDK `McpServer` reference](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServer.html).

## Prerequisites

- .NET 10 SDK
- `ModelContextProtocol.AspNetCore` 2.2.0 and `Microsoft.AspNetCore.TestHost` 10.0.11, restored from NuGet
- No MCP host, model account, credential, paid API, open port, or runtime network access

A production server might read an access token from a secret store through a placeholder such as `MCP_SERVER_TOKEN`. Never commit the value. This offline verifier does not read that variable or perform authentication.

## Run the verifier

From this folder:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
```

The verifier starts an ASP.NET Core `TestServer`, sends two concurrent `tools/call` requests through the real Streamable HTTP endpoint, and holds both handlers at a barrier before they read capabilities. One request declares `com.example/report-export`; the other declares no extension. A third malformed modern request omits the required client-capabilities metadata.

Expected output ends with:

```text
PASS: missing capabilities returned HTTP 400
PASS: missing capabilities returned MCP -32602
Verifier passed 6/6.
```

## Deterministic validation

Run the same checks used for the sample:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The sample uses fixed JSON, a one-shot concurrency barrier, an in-memory HTTP server, and exact response assertions. It does not use credentials, a database, the current clock, randomness, locale-sensitive comparison, files, external services, or a runtime network call. Restore and the vulnerability audit can contact configured NuGet sources. The barrier includes a five-second deadlock guard so a broken dispatch path fails instead of hanging forever.

## Expected behavior and limits

Both successful calls return HTTP 200. The request that advertises the extension reports `request=enabled`; the other reports `request=disabled`. Both report `server=null`, demonstrating why a stateless handler must read `JsonRpcRequest.Context.ClientCapabilities`. Omitting the required modern capability envelope is rejected before the tool runs with HTTP 400 and JSON-RPC code `-32602`.

Client capabilities describe protocol features; they are not authentication, authorization, or a trustworthy client identity. Use validated claims and application policy for access control. This sample covers request-scoped metadata isolation only. It does not test legacy initialize-era sessions, OAuth, Multi Round-Trip Requests, proxies, load balancers, or a deployed network server.
