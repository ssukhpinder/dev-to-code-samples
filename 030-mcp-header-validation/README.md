# MCP `x-mcp-header` validation

## Problem

The MCP 2026-07-28 Streamable HTTP transport lets a tool mark primitive input properties with `x-mcp-header`. A client mirrors those argument values into `Mcp-Param-*` request headers.

A conforming client must exclude a tool from `tools/list` when an annotation is invalid. That includes duplicate header suffixes, unsafe HTTP tokens, unsupported types, and annotations that cannot be reached from the schema root through `properties` alone. Header values also need the specification's Base64 sentinel encoding when plain ASCII would be unsafe or ambiguous.

This sample turns those rules into an offline executable check. It validates one tool input schema at a time, resolves nested argument paths, projects the headers, and verifies thirteen valid and invalid cases without an MCP server or model call.

## Prerequisites

- .NET 10 SDK
- No API key, account, model, network service, or other credential is required

## Setup

From this folder, restore the project:

```powershell
dotnet restore McpHeaderValidation.csproj --nologo
```

## Run

```powershell
dotnet run --project McpHeaderValidation.csproj --configuration Release
```

Expected final output:

```text
PASS: projects nested primitive arguments
PASS: omits an absent optional argument
PASS: omits an explicit null argument
PASS: base64-encodes unsafe and sentinel values
PASS: rejects duplicate header names
PASS: rejects number annotations
PASS: rejects unreachable annotations
PASS: ignores annotation-shaped literal data
PASS: rejects invalid HTTP tokens
PASS: normalizes integral JSON number forms
PASS: rejects rounded near-integer values
PASS: accepts JavaScript-safe integer boundaries
PASS: rejects integers outside both boundaries
13/13 checks passed
```

## Deterministic verification

Run the same gates used for the sample:

```powershell
dotnet restore McpHeaderValidation.csproj --nologo
dotnet format McpHeaderValidation.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build McpHeaderValidation.csproj --configuration Release --no-restore --nologo
dotnet run --project McpHeaderValidation.csproj --configuration Release --no-build
dotnet list McpHeaderValidation.csproj package --include-transitive
dotnet list McpHeaderValidation.csproj package --vulnerable --include-transitive
```

The executable exits nonzero on the first failed assertion. It covers:

- nested `properties` paths for `string`, JavaScript-safe `integer`, and `boolean` values;
- omission of an optional argument that is absent or explicitly `null`;
- UTF-8 Base64 encoding for non-ASCII, edge whitespace, and literal sentinel values;
- case-insensitive header-name uniqueness and RFC 9110 token syntax;
- rejection below `items`, composition keywords, and draft-07 schema dependencies without mistaking property-dependency arrays, `default`, or `examples` data for subschemas;
- rejection of the JSON Schema `number` type; and
- exact normalization of integral JSON forms, rejection of a high-precision near-integer, and both boundaries of the MCP integer range.

## Expected behavior

The valid fixture produces `Mcp-Param-Region`, `Mcp-Param-Tenant`, `Mcp-Param-Dry-Run`, and `Mcp-Param-Label`. The non-ASCII label is encoded as `=?base64?SGVsbG8sIOS4lueVjA==?=`. Each malformed fixture is passed to the projector separately, so its failure reason is deterministic.

## Limitations

This is a focused conformance fixture, not a complete JSON Schema 2020-12 implementation, a multi-tool `tools/list` filter, an MCP transport, or a `HeaderMismatch` server test. It checks the rules that affect `x-mcp-header` discovery and projection for one schema per call. Production clients should use a current official MCP SDK, and servers must still decode recognized headers and compare them with the request body before executing a tool.

Treat mirrored header values as untrusted routing metadata. Header/body equality is not authorization, and headers are visible to intermediaries. Do not annotate passwords, API keys, tokens, or personally identifiable information: Base64 is transport encoding, not encryption.
