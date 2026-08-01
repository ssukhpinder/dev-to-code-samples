# MCP `cacheScope` isolation

This sample reproduces a cross-user cache leak with two MCP clients, then fixes it by assigning a stable `cachePartition` to each authorization principal.

Both test servers deliberately advertise the same MCP server identity. Alice's server exposes an Alice-only tool and Bob's server exposes a Bob-only tool. When the clients share an `InMemoryResponseCacheStore` without partitions, Bob can receive Alice's cached private `tools/list` result. With partitions, each client receives only its own result.

## Prerequisites

- Node.js 20 or newer
- npm

The sample is fully local. It does not need an API key, network service, or paid model call.

## Setup

```shell
npm install
```

## Run the verification

```shell
npm run verify
```

To run only the deterministic test suite:

```shell
npm run build
npm test
```

## Expected behavior

The suite proves both sides of the boundary:

1. A shared store without `cachePartition` reproduces the private-result leak.
2. The same shared store with `cachePartition: "subject:alice"` and `cachePartition: "subject:bob"` keeps the private entries separate.

The passing vulnerability test is intentional: it makes the unsafe setup reproducible so the fixed setup can be compared against it.

## Limitations

- This is an in-process test of `tools/list`, not a production authentication system.
- `cacheScope` describes cache reuse; it does not grant or enforce access.
- Production partitions should use a stable, opaque authorization identity. Do not use raw access tokens or sensitive personal data as cache keys.
- Distributed caches also need tenant-safe key construction, lifecycle rules, invalidation handling, and operational controls.
