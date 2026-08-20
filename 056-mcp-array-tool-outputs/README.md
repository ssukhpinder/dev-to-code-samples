# MCP C# SDK array tool outputs

The MCP 2026-07-28 protocol allows `structuredContent` and `outputSchema` to use any JSON value at the root. A C# tool that returns an array or scalar can therefore put that natural value on the wire. Clients and contract tests that always read `structuredContent.result` will fail after they negotiate the current protocol.

This verifier runs the same two tools through the official MCP C# SDK twice: once with protocol `2026-07-28`, and once with the down-level `2025-11-25` protocol. It proves that the current wire shape is natural while the SDK keeps the old `{ "result": ... }` envelope for legacy clients.

## Prerequisites

- .NET SDK 10.0.303 or another compatible .NET 10 SDK
- Network access during `dotnet restore` to download `ModelContextProtocol` 2.2.0
- No MCP host, model, API account, or credentials

The project makes no runtime network calls. If you adapt the tools to call a protected service, use an environment variable such as `MCP_API_KEY=<your-key>`; never put the real value in source control.

## Restore and run

From this folder:

```powershell
dotnet restore
dotnet run --configuration Release --no-restore
```

The process exits with code 0 and prints:

```text
PASS 15/15
  2026-07-28: negotiated requested protocol
  modern: array schema has an array root
  modern: scalar schema has an integer root
  modern: array result has no result wrapper
  modern: scalar result has no result wrapper
  modern: legacy result-wrapper parser is rejected
  2026-07-28: array values survive serialization
  2026-07-28: text fallback remains available
  2025-11-25: negotiated requested protocol
  legacy: array schema advertises a result envelope
  legacy: scalar schema advertises a result envelope
  legacy: array result keeps the result envelope
  legacy: scalar result keeps the result envelope
  2025-11-25: array values survive serialization
  2025-11-25: text fallback remains available
```

For build-only validation:

```powershell
dotnet build --configuration Release --no-restore
dotnet format --verify-no-changes --no-restore
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

## What the verifier checks

Two in-memory duplex pipes connect an `McpClient` to an `McpServer`, so discovery and calls cross the actual SDK transport without opening a port. Both tools set `UseStructuredContent = true`: `list_tiers` returns a `string[]`, and `count_tiers` returns an `int`.

The modern client sees an array-root schema and array-valued `structuredContent`, plus an integer-root schema and numeric content. A deliberately old parser cannot find a `result` property. The legacy client sees object schemas with a required `result` property and matching wrapped values. Both versions also retain a text content block for compatibility.

The inputs, tool results, protocol versions, and assertion order are fixed. There is no clock, random value, external process, or service dependency, so repeated runs should produce byte-identical output.

## Limitations

This verifies the official MCP C# SDK 2.2.0 over its stream transport. It does not prove that every third-party host accepts every JSON Schema 2020-12 feature. Test the real host before shipping, and keep the text fallback when interoperability with older clients matters.

Primary references: the [MCP 2026-07-28 tools specification](https://modelcontextprotocol.io/specification/2026-07-28/server/tools) and the [MCP C# SDK tools guide](https://csharp.sdk.modelcontextprotocol.io/concepts/tools/tools.html).
