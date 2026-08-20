# MCP deterministic `tools/list`

MCP 2026-07-28 recommends returning tools in a deterministic order when the underlying tool set has not changed. This sample turns that boundary into an offline contract: it ordinal-sorts tool names, recursively sorts JSON object keys, serializes a canonical catalog, and computes a SHA-256 fingerprint.

## Problem

Registration order can vary after refactoring, dependency-injection changes, or a restart. If a server emits that incidental order, equivalent tool catalogs produce different JSON. That can make snapshots flaky and reduce prompt-cache hits even though the available tools did not change.

The verifier also guards two adjacent failure modes:

- Duplicate tool names fail before discovery output is emitted.
- Real metadata, schema, or authorization-scope changes produce a different fingerprint.

## Prerequisites

- .NET 10 SDK
- No credentials, model access, MCP client, or network service

The project has no package dependencies. It accepts the fully assembled JSON value from `result.tools`, preserves every tool field, and rejects duplicate tool names or duplicate JSON member names before hashing.

## Setup and validation

From this folder, run:

```bash
dotnet restore McpDeterministicToolsList.csproj
dotnet format McpDeterministicToolsList.csproj --verify-no-changes --no-restore
dotnet build McpDeterministicToolsList.csproj -c Release --no-restore
dotnet run --project McpDeterministicToolsList.csproj -c Release --no-build
dotnet list McpDeterministicToolsList.csproj package --include-transitive
dotnet list McpDeterministicToolsList.csproj package --vulnerable --include-transitive
```

Expected final output:

```text
Catalog fingerprint: <64 lowercase hex characters>
13/13 checks passed.
```

Run the verifier repeatedly to confirm the output is byte-for-byte stable:

```powershell
1..5 | ForEach-Object {
    dotnet run --project McpDeterministicToolsList.csproj -c Release --no-build
}
```

## What the checks prove

The program constructs equivalent catalogs with different tool registration order and different JSON object-property order. It verifies that:

- Raw serialization differs while canonical JSON and fingerprints match.
- Tool names use `StringComparer.Ordinal` ordering.
- JSON object keys are sorted recursively, including nested schema properties.
- Duplicate tool names and ambiguous duplicate JSON member names are rejected.
- Description and input-schema changes alter the fingerprint.
- The same authorization-scoped set stays stable, while a different set gets a different fingerprint.

## Limitations

This is a catalog contract test, not an MCP transport or conformance suite. It deliberately preserves array order, numeric JSON spellings, and Unicode code points, so it is not an implementation of RFC 8785 JSON Canonicalization Scheme. Its fingerprint is a change detector rather than a signature or trust mechanism. Assemble every paginated `tools/list` page before hashing. Servers may legitimately return different tool sets for different authorization inputs; partition caches by a non-secret authorization context and compare fingerprints only within the same scope. If descriptions or schemas contain unstable values, fix those values at their source instead of hiding the changes here.
