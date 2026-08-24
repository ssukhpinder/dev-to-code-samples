# MCP OAuth `iss` validation

## Problem

The final MCP 2026-07-28 authorization specification adds an issuer check to the OAuth callback. An MCP client must validate a returned `iss` value before it sends an authorization code to any token endpoint. That prevents an authorization-server mix-up from steering a code to the wrong server.

The rule is easy to weaken accidentally. A client must remember whether validated authorization-server metadata advertised `authorization_response_iss_parameter_supported`, reject a missing `iss` when it did, and compare every returned issuer with the recorded issuer even when support was not advertised. The comparison is ordinal: URI normalization is forbidden.

This sample implements that gate as a dependency-free C# function and verifies the complete MCP truth table offline.

## Prerequisites

- A stable .NET 10 SDK. `global.json` accepts the latest installed .NET 10 feature band and rejects prerelease SDKs.
- No MCP server, authorization server, browser, account, credential, paid API, container, or external service.
- No package dependencies or runtime network calls.

For a real integration, keep any client secret in a secret store or protected environment variable such as `MCP_CLIENT_SECRET`; never commit a value to the project.

## Setup and validation

Run these commands from this folder:

```powershell
dotnet --version
dotnet restore .\McpIssuerValidation.csproj
dotnet format .\McpIssuerValidation.csproj --verify-no-changes --no-restore
dotnet build .\McpIssuerValidation.csproj -c Release --no-restore
dotnet run --project .\McpIssuerValidation.csproj -c Release --no-build
dotnet list .\McpIssuerValidation.csproj package
dotnet list .\McpIssuerValidation.csproj package --vulnerable --include-transitive
```

The run prints one deterministic line for each case and ends with:

```text
13/13 issuer cases passed
```

## The validation contract

The validator receives three values:

1. The issuer recorded from a validated authorization-server metadata document before redirect.
2. The metadata value of `authorization_response_iss_parameter_supported`, represented as `true`, `false`, or absent.
3. The decoded `iss` query parameter, where `null` means absent and an empty string is still present.

It applies the specification table directly:

| Metadata advertises `iss` | Response `iss` | Result |
| --- | --- | --- |
| `true` | exact match | Proceed |
| `true` | absent | Reject missing issuer |
| any value | present mismatch | Reject issuer mismatch |
| `false` or absent | absent | Proceed |

The comparison uses `StringComparison.Ordinal`. Host case folding, default-port removal, trailing-slash changes, and percent-encoding normalization would all make a value compare differently and are intentionally not performed.

`MayProcessAuthorizationResponse` stays false for either rejection. A callback handler should check that gate before exchanging a code or acting on an OAuth error response.

## Expected behavior and limitations

The verifier covers advertised, unadvertised, and absent metadata flags; exact, absent, empty, and mismatched issuers; and several tempting URI-normalization mistakes. It never prints an authorization code, token, client secret, or server-provided error detail.

This is one callback boundary, not a complete OAuth client. The caller must obtain the expected issuer from validated metadata and bind it to the same per-request record as the PKCE verifier and `state`. Form decoding, redirect URI validation, token endpoint authentication, token and audience validation, TLS, secure storage, and replay protection remain separate requirements.

The client must also keep issuer identifiers unique across the authorization servers it uses. RFC 9207 forbids treating two different authorization servers as if they shared one issuer identifier; an equality check cannot distinguish servers that were registered under the same value.

MCP authorization applies to HTTP-based transports. Stdio clients should obtain credentials from their environment instead of copying this browser-based flow. If a mismatched `iss` accompanies an OAuth error, the specification also says not to act on or display `error`, `error_description`, or `error_uri`.

Primary references:

- [MCP 2026-07-28 authorization response validation](https://modelcontextprotocol.io/specification/2026-07-28/basic/authorization#authorization-response-validation)
- [MCP 2026-07-28 release notes](https://blog.modelcontextprotocol.io/posts/2026-07-28/#authorization)
- [RFC 9207 authorization server issuer identification](https://www.rfc-editor.org/rfc/rfc9207.html)
