# MCP resource not found error -32602

## Problem

The MCP 2026-07-28 specification requires `resources/read` to return JSON-RPC
`-32602` (`Invalid Params`) when a resource does not exist. Older protocol
revisions used `-32002` (`Resource Not Found`), which modern clients still need
to accept.

Returning an empty `contents` array is not a compatible shortcut for a miss. A
real resource can legitimately be empty, so that response cannot distinguish
"present but empty" from "not found."

This sample runs one resource server against modern and legacy clients through
the official MCP C# SDK. It verifies both error codes, a known text resource,
and a valid resource whose content list is empty.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- Network access during restore to download `ModelContextProtocol` 2.2.0.
- No MCP host, model, account, credential, paid service, or runtime network
  listener is required. The client and server communicate over in-memory pipes.

If an adapted server reads a protected catalog, use a placeholder environment
variable such as `MCP_API_KEY=<your-key>` and keep the real value out of source
control.

## Setup and validation

From this folder, run:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

The verifier exits nonzero on the first failed contract.

## Expected behavior

The run produces deterministic output:

```text
PASS 15/15
  modern: negotiated 2026-07-28
  modern: lists both real resources
  modern: reads known resource content
  modern: preserves valid empty contents
  modern: missing resource throws
  modern: missing resource uses -32602
  modern: client classifier accepts missing-resource code
  legacy: negotiated 2025-11-25
  legacy: lists both real resources
  legacy: reads known resource content
  legacy: preserves valid empty contents
  legacy: missing resource throws
  legacy: missing resource uses -32002
  legacy: client classifier accepts missing-resource code
  classifier rejects unrelated protocol errors
```

The classifier is intentionally scoped to an exception caught around
`ReadResourceAsync`. Do not treat every `-32602` in an MCP client as a missing
resource; the same JSON-RPC code represents other invalid parameters too.

## Limitations

- The verifier covers `ModelContextProtocol` 2.2.0 and the stream transport. It
  does not prove how every third-party host renders protocol errors.
- It does not open an HTTP listener or test authentication, authorization,
  URI-path sanitization, caching, or retry policy.
- A production client should preserve the requested URI in its diagnostic
  context and avoid exposing sensitive resource identifiers in logs.

See the final [MCP 2026-07-28 resources specification](https://modelcontextprotocol.io/specification/2026-07-28/server/resources)
and the [MCP C# SDK resource documentation](https://csharp.sdk.modelcontextprotocol.io/concepts/resources/resources.html)
for the protocol and API contracts.
