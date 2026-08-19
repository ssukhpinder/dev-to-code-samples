# MCP C# SDK hybrid sessions

## Problem

MCP C# SDK 2.2.0 adds `HttpServerSessionMode.StatefulForInitializeClients` for a specific migration problem. An existing HTTP server may still need sessions for clients that use the `2025-11-25` `initialize` handshake, while newer `2026-07-28` clients expect the sessionless protocol on the same endpoint.

Choosing `Stateful` rejects modern protocol requests so a dual-path client can downgrade; a modern-only client fails. Choosing `Stateless` removes the session behavior that legacy clients may still need. Hybrid mode decides per request: initialize-era clients get a session, and modern clients stay stateless.

This sample hosts one MCP endpoint in ASP.NET Core `TestServer` and verifies both paths without opening a port:

- a `2026-07-28` discovery and tool call succeed without `Mcp-Session-Id`;
- a `2025-11-25` initialize response creates a session, and a tool call reuses it;
- modern `DELETE` returns `405 Method Not Allowed`; and
- legacy `DELETE` closes the session, and reusing its ID returns `404 Not Found`.

## Prerequisites

- .NET 10 SDK
- network access for the first NuGet restore

No API key, model call, account, external MCP server, paid service, or other credential is required.

## Setup

From this folder:

```powershell
dotnet restore .\McpHybridSessions.csproj --nologo
dotnet build .\McpHybridSessions.csproj --configuration Release --no-restore --nologo
```

The project pins the stable `ModelContextProtocol.AspNetCore` package at `2.2.0` and uses `Microsoft.AspNetCore.TestHost` so every request stays in process.

## Run and verify

Run the executable verifier:

```powershell
dotnet run --project .\McpHybridSessions.csproj --configuration Release --no-build
```

Expected output:

```text
PASS: modern 2026-07-28 discovery returned no session
PASS: modern tool call stayed stateless
PASS: legacy initialize minted a session
PASS: legacy tool call reused its session
PASS: modern DELETE was rejected without session state
PASS: legacy DELETE closed the session
6/6 checks passed
```

## Deterministic validation

Run all sample gates with:

```powershell
dotnet restore .\McpHybridSessions.csproj --nologo
dotnet format .\McpHybridSessions.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build .\McpHybridSessions.csproj --configuration Release --no-restore --nologo
dotnet run --project .\McpHybridSessions.csproj --configuration Release --no-build
dotnet list .\McpHybridSessions.csproj package --include-transitive
dotnet list .\McpHybridSessions.csproj package --vulnerable --include-transitive
```

The verifier uses fixed protocol versions, request bodies, and assertions. It exits nonzero on the first unexpected status, session header, or tool result. After the legacy `DELETE`, it sends one more request with the old ID and requires `404 Not Found`.

## Expected behavior and limitations

Both client eras share `/mcp`, but they do not share session semantics. A modern request never gains a session just because a legacy client used the endpoint. A legacy initialize flow still receives the session ID needed for later requests.

Hybrid mode is a migration tool, not a reason to add sessions to a server that does not need them. The `2026-07-28` side still cannot use unsolicited notifications, resource subscriptions, or other session-only behavior. Legacy sessions still consume memory, disappear on restart, and may require affinity in a multi-instance deployment.

The sample verifies transport behavior with fixed JSON-RPC messages. It does not cover authentication, authorization, legacy SSE, MRTR, session migration, distributed deployment, or production load balancing.
