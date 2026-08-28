# Anthropic Skills API delete migration guard

## Problem

The Anthropic Skills API is generally available without the
`skills-2025-10-02` beta header. That migration changes one destructive
contract: the beta API refused `DELETE /v1/skills/{skill_id}` while versions
existed, but the GA API deletes the Skill and every version in one call.

Current SDKs can expose the GA behavior through both `client.skills` and the
compatibility `client.beta.skills` namespace. Code that treated the old HTTP
400 response as a safety check therefore needs an explicit guard before it
drops the header or updates its SDK.

This deterministic console sample models both delete contracts and adds a
guard that inventories every version page, requires explicit cascade approval,
and rejects an approval when the version inventory has changed.

## Prerequisites

- A stable .NET 10 SDK
- No Anthropic account, API key, runtime network call, or model call

## Setup and run

Run these commands from this folder:

```powershell
dotnet restore .\AnthropicSkillsDeleteGuard.csproj
dotnet run --project .\AnthropicSkillsDeleteGuard.csproj -c Release
```

Expected output:

```text
Beta delete: HTTP 400, retained 3 versions
GA raw delete: HTTP 200, removed 3 versions
Guard preview: skver_001, skver_002, skver_003
Guard without cascade approval: ApprovalRequired
Changed inventory: InventoryChanged
Guard with fresh approval: Deleted, approved 4 observed versions
Checks passed: 7/7
```

## What the verifier covers

`FakeSkillsApi` uses fixed in-memory Skills and opaque page cursors. It proves:

1. The beta-header contract refuses to delete a Skill that still has versions.
2. The GA contract deletes the same Skill and all three versions.
3. `SkillsDeleteGuard` follows every `next_page` value without parsing it.
4. A non-empty version inventory requires an explicit `allowCascade` decision.
5. A new version invalidates an older approval before the delete call.
6. A fresh explicit approval allows one GA delete for a four-version plan.

The fake isolates migration logic; it is not an Anthropic SDK or API emulator.

## Deterministic validation

```powershell
dotnet restore .\AnthropicSkillsDeleteGuard.csproj
dotnet format .\AnthropicSkillsDeleteGuard.csproj --verify-no-changes --no-restore
dotnet build .\AnthropicSkillsDeleteGuard.csproj -c Release --no-restore
dotnet run --project .\AnthropicSkillsDeleteGuard.csproj -c Release --no-build
dotnet list .\AnthropicSkillsDeleteGuard.csproj package --include-transitive
dotnet list .\AnthropicSkillsDeleteGuard.csproj package --vulnerable --include-transitive
```

The executable verifier reads no clock, randomness, files, credentials,
locale-sensitive input, or external state. Repeated runs produce identical
output. Restore and the vulnerability audit can contact configured NuGet
sources; the audit result reflects the advisory data available at run time.

## Credentials and live adaptation

No credentials are required for this sample. A live client would read a
placeholder such as `ANTHROPIC_API_KEY=<your-api-key>` from an environment
variable or secret store; never place the value in source control.

When adapting the guard, replace `ISkillsApi` with the supported SDK or HTTP
client, list every version page, show the planned version IDs to the operator,
and only then call the GA delete endpoint after an explicit cascade decision.

## Limitations

The preview and delete are separate requests. Rechecking immediately before
delete narrows the race window, but it cannot provide an atomic compare-and-
delete guarantee if another writer can add a version between those calls.
Use workspace isolation, access controls, and an operational audit trail when
that residual risk matters. The sample also omits retries, authentication,
rate-limit handling, and real HTTP response parsing.

## Primary sources

- [Anthropic migration guide for `skills-2025-10-02`](https://platform.claude.com/docs/en/build-with-claude/skills-guide#migrate-from-skills-2025-10-02)
- [Claude Platform release notes](https://platform.claude.com/docs/en/release-notes/overview)
- [List Skill Versions API](https://platform.claude.com/docs/en/api/skills/versions/list)
