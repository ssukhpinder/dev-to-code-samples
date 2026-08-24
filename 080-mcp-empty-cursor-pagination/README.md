# MCP pagination with an empty `nextCursor`

An MCP client can lose most of a tool, prompt, or resource catalog when it uses
`string.IsNullOrEmpty(nextCursor)` to decide that pagination is finished. In the
MCP 2026-07-28 specification, a cursor is opaque, an empty string is valid, and
only a missing or `null` `nextCursor` marks the end of the result set.

This deterministic console sample runs a broken loop and a corrected loop
against the same three-page fake `tools/list` server. Page one returns an empty
cursor, page two returns the opaque token `opaque:/+==`, and page three omits
the next cursor. The sample also verifies the recommended `-32602` response
for an invalid cursor.

## Prerequisites

- A stable .NET 10 SDK
- No MCP server, network connection, account, or credential

## Setup and run

```bash
dotnet restore McpEmptyCursorPagination.csproj
dotnet run --project McpEmptyCursorPagination.csproj -c Release
```

Expected output:

```text
Broken tools: catalog.search
Fixed tools: catalog.search, catalog.lookup, catalog.health
Forwarded cursors: <missing> | <empty> | opaque:/+==
Invalid cursor code: -32602
Checks passed: 8/8
```

The first request uses `null` to represent an omitted cursor. After every
response, the fixed loop tests `page.NextCursor is null`; it never parses,
normalizes, decodes, or applies a truthiness rule to a non-null token.

## Deterministic validation

Run these commands from this folder:

```bash
dotnet restore McpEmptyCursorPagination.csproj
dotnet format McpEmptyCursorPagination.csproj --verify-no-changes --no-restore
dotnet build McpEmptyCursorPagination.csproj -c Release --no-restore
dotnet run --project McpEmptyCursorPagination.csproj -c Release --no-build
dotnet list McpEmptyCursorPagination.csproj package --include-transitive
dotnet list McpEmptyCursorPagination.csproj package --vulnerable --include-transitive
```

The verifier uses fixed in-memory pages, so repeated runs produce identical
output. It makes no paid calls and reads no files, clock, randomness, locale,
or environment-specific state.

## Credentials and live adaptation

No credentials are required. If this loop is adapted to a real HTTP transport,
keep values such as `MCP_SERVER_URL=<server-url>` and
`MCP_ACCESS_TOKEN=<token>` in environment variables or a secret store rather
than source control. Every 2026-07-28 request must also include the required
protocol `_meta`; the fake pager intentionally isolates cursor handling.

## Limitations

This is a cursor-control verifier, not a complete MCP client or server. It does
not implement JSON-RPC framing, transports, authentication, cache metadata,
retries, or concurrent catalog changes. The corrected loop adds a configurable
page cap and accepts a cancellation token; production code should also define
its retry and partial-result policy.

## Primary sources

- [MCP 2026-07-28 pagination specification](https://modelcontextprotocol.io/specification/2026-07-28/server/utilities/pagination)
- [MCP 2026-07-28 release announcement](https://blog.modelcontextprotocol.io/posts/2026-07-28/)
