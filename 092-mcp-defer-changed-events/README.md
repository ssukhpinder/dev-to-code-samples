# MCP C# `DeferChangedEvents` batching

Loading several dynamic MCP tools one by one can raise one collection-change event per mutation. A notification handler can then ask clients to refresh `tools/list` repeatedly and can expose intermediate catalog states while a plug-in pack is still loading.

The stable MCP C# SDK provides `McpServerPrimitiveCollection<T>.DeferChangedEvents()` for this boundary. Change signals raised inside the scope are suppressed until the final active scope is disposed, then coalesced into one `Changed` event. This sample verifies the behavior without starting an MCP host or opening a network connection.

The API is documented in the official [`McpServerPrimitiveCollection<T>` reference](https://csharp.sdk.modelcontextprotocol.io/api/ModelContextProtocol.Server.McpServerPrimitiveCollection-1.html), and the stable [C# SDK 2.0 release](https://github.com/modelcontextprotocol/csharp-sdk/releases/tag/v2.0.0) records the batching feature. The sample pins the current stable `ModelContextProtocol.Core` 2.2.0 package.

## Prerequisites

- .NET 10 SDK
- `ModelContextProtocol.Core` 2.2.0, restored from NuGet
- No MCP host, account, credential, model call, database, open port, or runtime network access

If a real server needs a credential, keep it outside source control in a secret store or an environment variable such as `MCP_SERVER_TOKEN=<replace-me>`. This verifier does not read that variable.

## Run the sample

From this folder:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
```

Expected output ends with:

```text
PASS: exception disposal flushed one real change
PASS: concurrent additions produced one deterministic change
Verifier passed 8/8.
```

The checks compare ordinary additions with one deferred scope, nested scopes, an empty scope, an empty `Clear`, duplicate `TryAdd`, exception-safe disposal, and concurrent additions. The sample uses real `McpServerTool` instances and the SDK collection rather than reimplementing the coalescing logic.

## Deterministic validation

Run the complete validation set:

```bash
dotnet restore
dotnet format McpDeferChangedEvents.csproj --verify-no-changes --no-restore
dotnet build McpDeferChangedEvents.csproj --configuration Release --no-restore
dotnet run --project McpDeferChangedEvents.csproj --configuration Release --no-build
dotnet list McpDeferChangedEvents.csproj package --include-transitive
dotnet list McpDeferChangedEvents.csproj package --vulnerable --include-transitive
```

Five repeated verifier runs should exit with code 0 and produce identical normalized output. The fixture uses fixed tool names and exact counts. It does not depend on credentials, external services, current time, randomness, locale, files, or runtime network access. Restore and the vulnerability audit can contact configured NuGet sources.

## Expected behavior and limits

Three ordinary additions raise three `Changed` events. The same additions inside `DeferChangedEvents()` raise none while the scope is active and exactly one after disposal. Nested scopes wait for the outermost disposal. Empty scopes and rejected duplicate additions raise nothing. `Clear()` is a signal-producing operation even when the collection is already empty, so its deferred scope emits one event without an item-count delta. If a mutation happens before an exception, disposing the scope still raises one event. Concurrent mutations are supported and coalesced.

This sample measures the collection event, not a deployed client's number of refresh requests. Application code still decides how `Changed` maps to MCP notifications, and modern stateless HTTP clients receive change notifications through the protocol's subscription flow rather than unsolicited messages. Keep a deferred scope short: clients should see a complete catalog after disposal, and unrelated long-running work should happen outside it.
