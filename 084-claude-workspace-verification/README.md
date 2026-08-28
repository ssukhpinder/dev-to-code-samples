# Claude API workspace verification

This .NET 10 console sample verifies the `anthropic-workspace-id` header on a successful Claude API response before returning its body to application code. It catches requests that resolve to a different workspace than the one the application intended to use.

The verifier is entirely offline. A fake `HttpMessageHandler` returns deterministic responses, so no Anthropic account, API credential, network request, or paid model call is needed.

## Problem

A multi-workspace API key can select a workspace with the `anthropic-workspace-id` request header. The successful response contains the workspace that Anthropic actually resolved. If an application records only its routing configuration, a stale tenant mapping or bad deployment setting can misattribute usage and workspace-scoped resources.

`WorkspaceVerifiedClient` accepts a routing target plus an independently sourced authorized workspace. It rejects a mismatch before sending, then requires one well-formed response value and compares it with the authorized workspace before exposing the body. The two inputs must not be aliases for the same unchecked setting.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Setup, build, and run

From this folder:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore --configuration Release
dotnet run --no-build --configuration Release
```

Expected output:

```text
PASS matching workspace passes and request is scoped
PASS stale routing configuration makes no request
PASS wrong workspace fails before body use
PASS missing workspace on success fails closed
PASS malformed response workspace fails closed
PASS invalid configured workspace makes no request
PASS authentication failure remains the primary error
7 deterministic checks passed.
```

For deterministic repetition, run the final command several times and compare its output. The sample has no clock, randomness, locale, file, or external-service dependency.

## What the sample verifies

- The outbound request carries the configured `anthropic-workspace-id` value.
- A stale routing target is rejected against an independently owned authorization mapping before the transport runs.
- A successful response is accepted only when its workspace ID is present, singular, well formed, and exactly equal to the authorized value.
- A mismatched workspace is rejected before the caller can parse or persist the body.
- Invalid configuration fails before the HTTP transport runs.
- A 401 remains an HTTP authentication failure because Anthropic can omit the workspace header when authentication does not complete.

## Adapting it to a live integration

Load routing and authorization values from independently governed sources, such as deployment routing plus a tenant registry or environment allowlist. The workspace ID is not an API secret, but the API key is and must never be committed:

```powershell
$env:ANTHROPIC_API_KEY = '<read from your secret store>'
$env:ANTHROPIC_TARGET_WORKSPACE_ID = 'wrkspc_<routing-id>'
$env:AUTHORIZED_WORKSPACE_ID = 'wrkspc_<independently-approved-id>'
```

Use the official SDK's raw-response accessor, or keep the same check in an HTTP delegating layer. Record the verified response workspace next to the provider request ID so usage, cost, and workspace-scoped resources can be traced without storing credentials.

## Limitations

This sample does not call Anthropic, implement credential loading, retry failures, parse a Messages response, or prove organization membership. It verifies one authorized workspace per request; applications that intentionally route among several workspaces need an independently governed request-to-workspace mapping. Duplicating one configuration value into both arguments defeats the stale-mapping check, although response verification still detects provider resolution that differs from that value.

Do not require the header for Admin API requests or failed requests that never completed authentication. The header can be absent in those cases. The Default Workspace still has a real `wrkspc_` ID in response headers even though some Admin API and usage-report fields represent it as `null`.

## References

- [Anthropic: Workspaces](https://platform.claude.com/docs/en/manage-claude/workspaces)
- [Anthropic: API overview and response headers](https://platform.claude.com/docs/en/api/overview)
