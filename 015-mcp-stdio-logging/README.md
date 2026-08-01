# MCP stdio logging contract

A local MCP server owns `stdout` as its newline-delimited JSON-RPC channel. One `console.log()` call can become the first line a client reads and make an otherwise healthy server look like it failed during protocol negotiation.

This sample makes that failure reproducible. It launches two TypeScript servers through the SDK 2.0 `serveStdio(...)` entry point with the stable `@modelcontextprotocol/server` 2.0.0 package:

- `src/bad-server.ts` writes a startup message to `stdout`.
- `src/good-server.ts` writes a structured diagnostic to `stderr`.
- `test/stdio-contract.test.ts` sends a real MCP `server/discover` request for protocol version `2026-07-28` and inspects the first line on each stream.

## Prerequisites

- Node.js 20 or later
- npm

No model, API key, network service, or paid request is needed after dependencies are installed.

## Setup

```bash
npm install
```

## Run and test

```bash
npm run verify
```

The verification command checks formatting, type-checks, builds, and runs the two offline regression tests. The bad server test passes by proving its first `stdout` line is not JSON. The good server test passes only when `stdout` contains a valid `server/discover` response and the human-readable diagnostic arrives on `stderr`.

To run the corrected server directly after a build:

```bash
npm run build
npm start
```

An MCP host should launch `build/src/good-server.js`. Starting that process in a terminal leaves it waiting for JSON-RPC requests on `stdin`, which is expected.

## Expected behavior

- Every complete line on the good server's `stdout` parses as an MCP JSON-RPC message.
- The good server's startup diagnostic is emitted on `stderr` and does not corrupt the protocol channel.
- The intentionally bad server's first `stdout` line fails JSON parsing, demonstrating the regression the test prevents.

## Limitations

This sample checks stdio framing, not Streamable HTTP logging or production log shipping. The test asserts the first line because startup logs are a common failure mode; a production transport harness can extend the same rule to every `stdout` line for the lifetime of the process. Logs must also be sanitized before they are sent to `stderr` or any collector.

Article: _link added after publication_
