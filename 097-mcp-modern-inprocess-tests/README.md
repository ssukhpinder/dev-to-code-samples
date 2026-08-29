# MCP TypeScript SDK v2 in-process tests

This sample compares two socket-free MCP test harnesses against the same tool. It shows that `InMemoryTransport.createLinkedPair()` exercises the 2025-era lifecycle, while an injected `createMcpHandler.fetch` path can pin and verify the final `2026-07-28` protocol era.

## Problem

An in-memory client/server pair can prove tool behavior while still missing the protocol path used in production. In MCP TypeScript SDK v2, directly connected `Client` and `McpServer` instances remain on the 2025-era handshake. A test suite built only on that pair can stay green even when a strict modern handler rejects the same default client.

The modern harness in this folder uses the SDK's real HTTP client transport, but injects the handler's `fetch` method. The URL uses the reserved `.invalid` domain and is never dialed.

## Prerequisites

- Node.js 20 or later
- Network access only for `npm ci` and the optional dependency audit

No MCP host, open port, DNS lookup, credential, account, model call, database, clock, or random input is required at runtime.

## Setup, run, and test

```bash
npm ci
npm run format:check
npm run typecheck
npm run build
npm run verify
npm test
npm audit --omit=dev
```

## Expected behavior

`npm run verify` prints:

```text
PASS: 10/10 in-process MCP era checks
```

The verifier proves that:

- the linked in-memory pair reports `legacy`;
- the injected-fetch client, pinned to `2026-07-28`, reports `modern`;
- both harnesses list and call the same deterministic `normalize-ticket` tool;
- modern requests use the injected `fetch` path and never carry `Mcp-Session-Id`;
- a handler configured with `legacy: "reject"` rejects a default legacy HTTP client.

The package pins `@modelcontextprotocol/client` and `@modelcontextprotocol/server` 2.0.0, the stable v2 packages for the final MCP `2026-07-28` revision.

## Limitations

This is an in-process HTTP contract test, not a deployment test. It does not cover framework adapters, reverse proxies, TLS, CORS, authorization, streaming cancellation, or a separately deployed process. Stdio coverage still requires spawning the server, and a dual-era deployment should keep explicit tests for both accepted eras.

The sample verifies one tool and a strict version boundary. Extend the factory with the resources, prompts, middleware, and error paths your actual handler exposes.

## References

- [MCP TypeScript SDK: test a server](https://ts.sdk.modelcontextprotocol.io/v2/testing.html)
- [MCP TypeScript SDK: support protocol revision 2026-07-28](https://ts.sdk.modelcontextprotocol.io/v2/migration/support-2026-07-28)
- [MCP 2026-07-28 specification release](https://blog.modelcontextprotocol.io/posts/2026-07-28/)
