# MCP TypeScript SDK v2 invalid-token migration

This sample verifies how MCP TypeScript SDK v2 classifies access-token failures. It calls the web-standard bearer-auth gate with in-memory `Request` objects, so it never starts a server or contacts an authorization service.

## Problem

MCP TypeScript SDK v2 consolidates the v1 OAuth error classes. A token verifier that still throws the legacy `InvalidTokenError`, or replaces it mechanically with a generic `Error`, is treated as an unexpected failure. `requireBearerAuth` then returns HTTP 500 `server_error` instead of HTTP 401 `invalid_token` with a `WWW-Authenticate` challenge.

The v2 verifier must throw:

```typescript
throw new OAuthError(
  OAuthErrorCode.InvalidToken,
  "token is expired or revoked",
);
```

## Prerequisites

- Node.js 20 or later
- Network access only for `npm ci`

No MCP host, OAuth server, access token, account, or credential is required. Every token in the source is an inert fixture value.

## Setup, run, and test

```bash
npm ci
npm run format:check
npm run typecheck
npm run build
npm run verify
npm test
```

## Expected behavior

`npm run verify` prints `PASS: 13/13 bearer-auth checks`, and `npm test` reports two passing tests. The verifier proves four paths:

- a generic verifier error becomes HTTP 500 `server_error`;
- `OAuthErrorCode.InvalidToken` becomes HTTP 401 `invalid_token`;
- the 401 includes a Bearer challenge and protected-resource metadata;
- valid token metadata passes through, while a missing scope becomes HTTP 403 `insufficient_scope`.

The sample pins `@modelcontextprotocol/server` 2.0.0, the stable v2 line that implements the MCP 2026-07-28 revision.

## Limitations

This is a middleware contract test, not a complete authorization deployment. It does not validate JWT signatures, call token introspection, serve protected-resource metadata, run an authorization code flow, or test a framework adapter. Production verifiers still need issuer, audience, resource, expiry, and scope checks appropriate to their token format.

## References

- [MCP TypeScript SDK v2 migration guide](https://github.com/modelcontextprotocol/typescript-sdk/blob/main/docs/migration/upgrade-to-v2.md#auth)
- [MCP TypeScript SDK authorization guide](https://github.com/modelcontextprotocol/typescript-sdk/blob/main/docs/serving/authorization.md)
- [MCP TypeScript SDK v2 bearer-auth API](https://ts.sdk.modelcontextprotocol.io/v2/api/%40modelcontextprotocol/server/server/middleware/bearerAuth.html)
