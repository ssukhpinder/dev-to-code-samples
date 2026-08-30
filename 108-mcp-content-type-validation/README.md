# MCP TypeScript SDK v2 Content-Type validation

An MCP HTTP endpoint should reject a missing or misleading request media type before any tool runs. This sample drives the real `createMcpHandler` entry point with raw Web `Request` objects and turns that boundary into an eight-case, offline contract test.

## Problem

Hand-written clients, gateways, and test adapters can send a valid JSON-RPC body with an invalid `Content-Type`. A substring check is unsafe: `text/plain; a=application/json` contains the expected text but is not an `application/json` media type. Treating it as JSON lets a malformed request reach tool dispatch.

The SDK v2 server entry validates the parsed media type. This sample proves both halves of the contract:

- accepted JSON variants return HTTP 200 and dispatch exactly once;
- missing, spoofed, prefixed, and suffix-only values return HTTP 415 and never dispatch.

## Prerequisites

- Node.js 20 or later
- npm (bundled with Node.js)

No credentials or network service are required at runtime. The `.invalid` URL is only an origin for the in-process `Request`; it is never dialed.

## Setup and run

```bash
npm ci
npm run verify
```

Expected output:

```text
[PASS] exact application/json: 200, dispatched=1
[PASS] JSON with charset: 200, dispatched=1
[PASS] mixed-case JSON: 200, dispatched=1
[PASS] JSON with trailing semicolon: 200, dispatched=1
[PASS] substring spoof: 415, dispatched=0
[PASS] JSON prefix subtype: 415, dispatched=0
[PASS] missing Content-Type: 415, dispatched=0
[PASS] structured suffix only: 415, dispatched=0
PASS: 8/8 Content-Type checks passed
```

## Test and inspect

```bash
npm run format:check
npm run typecheck
npm run build
npm test
```

The matrix lives in `src/content-type-contract.ts`. Each case gets a fresh server and handler, sends the same deterministic `tools/call` request, and records the HTTP status plus the tool invocation delta. The test suite checks the whole matrix and singles out the spoofed value as a regression guard.

## Expected behavior and limitations

The accepted values are `application/json`, a charset parameter, mixed casing, and a trailing semicolon. The rejected values are `text/plain; a=application/json`, `application/json-seq`, a missing header, and `application/problem+json`. Rejected cases must leave the dispatch counter at zero.

This is a server-entry contract test, not a conformance suite. It does not cover authentication, sessions, streaming responses, reverse-proxy rewriting, or browser CORS behavior. It intentionally tests the SDK's exact `application/json` policy; accepting structured syntax suffixes such as `application/problem+json` would be a separate application decision.

## References

- [MCP TypeScript SDK v2 migration guide](https://ts.sdk.modelcontextprotocol.io/v2/migration/upgrade-to-v2)
- [MCP Streamable HTTP transport specification](https://modelcontextprotocol.io/specification/2026-07-28/basic/transports/streamable-http)
