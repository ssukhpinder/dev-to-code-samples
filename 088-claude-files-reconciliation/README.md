# Claude Files API `ids[]` reconciliation

This sample demonstrates a narrow failure mode in the stable Claude Files API: a
successful `GET /v1/files?ids[]=...` response silently omits IDs that do not
resolve to a visible file. Treating HTTP 200 as proof that every requested file
exists can therefore pass incomplete work downstream.

The console app builds the current `ids[]` request shape, intercepts it with an
in-memory HTTP handler, parses a fixture in which one requested ID is absent,
and blocks downstream work unless the requested and returned ID sets reconcile
exactly. It also
guards against unexpected IDs, duplicate response IDs, and a non-null
`next_page` value.

## Prerequisites

- .NET SDK 10.0.100 or later
- No Claude account or API key for the offline verifier

## Setup and validation

From this folder, run:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --configuration Release --no-restore
dotnet run --configuration Release --no-build
dotnet list package
dotnet list package --vulnerable --include-transitive
```

The verifier makes no network request. Expected final output:

```text
Returned: file_01ACTIVEALPHA000000000000, file_03ACTIVECHEETAH000000000
Missing:  file_02DELETEDBRAVO0000000000
Decision: BLOCK
Verifier: 8/8 passed
```

## How it works

`ClaudeFilesVerifier.BuildListUri` accepts 1-100 distinct, non-empty opaque IDs.
It emits one encoded `ids[]` query value per ID and never adds `page` or `limit`.
`ReconcileAsync` uses ordinal string comparison, requires `next_page` to be
`null`, and reports missing, unexpected, and duplicate IDs separately.

`FixtureHandler` guarantees deterministic verification by intercepting the
request in memory. The synthetic `x-api-key` value is a placeholder and is
never sent anywhere.

To adapt this pattern for a live integration, use a normal `HttpClient`, read a
real key from a secret store or the `ANTHROPIC_API_KEY` environment variable,
and keep the `anthropic-version: 2023-06-01` header. Do not send the legacy
`anthropic-beta: files-api-2025-04-14` header when using the current `ids[]`
contract.

## Limitations

- The fixture proves request construction and reconciliation, not service
  availability, authentication, rate limits, or live workspace contents.
- A missing ID means it did not resolve to a visible file for the current
  workspace; it does not identify the reason.
- File IDs are server-side references, not an end-user authorization boundary.
  Keep your own user-to-file mapping and use separate workspaces when tenant
  isolation is required.
- This pattern checks a known set of up to 100 IDs. Use normal `page` /
  `next_page` pagination when discovering all files.

No credentials, paid model calls, database, browser, or external service are
required.
