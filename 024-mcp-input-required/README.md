# MCP `InputRequiredException` for stateless elicitation

This .NET 10 console verifier demonstrates the MCP C# SDK 2.0 pattern for a
tool that needs confirmation while running on a stateless server.

Under MCP `2026-07-28`, a stateless handler cannot use `ElicitAsync`. It throws
`InputRequiredException` instead. The SDK emits an `input_required` result, the
client gathers the answer, and the original tool call is retried with
`inputResponses` and the opaque `requestState`.

The sample compiles a real `[McpServerTool]` handler, HMAC-seals its retry state,
and deterministically checks seven paths without starting a server or making a
model call:

1. The first call throws `InputRequiredException`.
2. An accepted, checked confirmation with valid state proceeds.
3. Only the named `confirm` field counts.
4. Decline and cancel both stop.
5. A retry missing the named approval response stops.
6. A tampered state token is rejected.
7. A token is rejected at its expiry boundary.

## Prerequisites

- .NET 10 SDK
- Network access for the initial NuGet restore
- No API keys, MCP host, model, or paid service

## Restore and run

From this folder:

```shell
dotnet restore McpInputRequired.csproj
dotnet run --project McpInputRequired.csproj --configuration Release
```

Expected final output:

```text
PASS first call requests bound approval
PASS valid accepted confirmation proceeds
PASS only the named confirm field counts
PASS decline and cancel stop
PASS retry without approval stops
PASS tampered state is rejected
PASS expired state is rejected
7/7 checks passed
```

## Validate

```shell
dotnet format McpInputRequired.csproj --verify-no-changes --no-restore
dotnet build McpInputRequired.csproj --configuration Release --no-restore
dotnet run --project McpInputRequired.csproj --configuration Release --no-build
dotnet list McpInputRequired.csproj package --vulnerable --include-transitive
```

## Expected behavior

`DeploymentTools.ConfirmDeployment` first checks for the retried
`inputResponses`. If none exist and the client supports multi-round-trip
requests, it throws `InputRequiredException` with one boolean elicitation and an
integrity-protected state value. The token binds the operation, environment, and
expiry. On retry the handler verifies that state first, reads only the named
`confirm` field, and treats every path except an explicit accepted and checked
answer as not approved.

## Limitations

This is a protocol-flow verifier, not a deployment system. It does not host an
HTTP endpoint, authenticate users, authorize environments, persist approvals,
prevent replay, or perform a real deployment. The in-memory HMAC key is generated
only for this verifier. Production code must load a protected, shared key; bind
the state to the original principal and every security-relevant argument; rotate
keys deliberately; and use a store when single-use approval is required.
