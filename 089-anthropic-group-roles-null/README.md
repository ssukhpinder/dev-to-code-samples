# Anthropic Admin API group roles `null`

The Claude Enterprise Admin API uses two different role-list shapes with different meanings:

- `"roles": []` means the group has no attached custom roles.
- `"roles": null` means role data was temporarily unavailable and the read should be retried.

Collapsing both shapes with `roles ?? []` can let a degraded response pass an access audit as an intentionally empty group. This sample parses raw list-group response fixtures into `Attached`, `Empty`, or `Degraded` states and refuses to complete an audit while any group is degraded.

Anthropic moved its Enterprise user-management endpoints out of beta on August 19, 2026. The current [user-management guide](https://platform.claude.com/docs/en/manage-claude/user-management) documents the `null` retry rule, and the [release notes](https://platform.claude.com/docs/en/release-notes/overview) document the generally available release. Enterprise user-management endpoints are not in the current SDK rollout, so the sample deliberately validates raw JSON without claiming SDK support.

## Prerequisites

- .NET 10 SDK
- No Claude Enterprise account, Admin API key, paid API call, or network access at runtime

A live integration would keep an Admin API key in a secret store and expose it at runtime through a placeholder such as `ANTHROPIC_ADMIN_KEY`; never commit the value. The offline sample does not read that variable.

## Run the verifier

From this folder:

```bash
dotnet restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
```

The verifier exercises attached roles, an intentionally empty list, a temporarily unavailable `null`, a healthy audit, a blocked audit, preserved pagination metadata, and malformed response shapes. It ends with:

```text
Verification passed: 13/13
```

## Deterministic validation

Run the same checks used for the sample:

```bash
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
dotnet list package --include-transitive
dotnet list package --vulnerable --include-transitive
```

All response bodies are embedded fixtures. The verifier does not use the clock, randomness, locale-sensitive comparison, a file fixture, credentials, or a runtime network call. Restore and the vulnerability audit can contact configured NuGet sources.

## Expected behavior

The code preserves API ordering and role IDs, but it does not treat role order as a permission rule. An attached list and an empty list can be reconciled normally. A `null` role value produces a `Degraded` state, records the affected group ID, and sets `CanComplete` to `false` so the caller can retry the read instead of making an authorization or remediation decision from incomplete data.

This verifier covers response classification, not authentication, retry timing, pagination orchestration, permission expansion, SCIM synchronization, or writes to Claude Enterprise. Production code still needs bounded backoff, observability, secure key handling, and a policy for repeated degraded reads.
