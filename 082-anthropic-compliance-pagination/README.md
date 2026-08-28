# Anthropic Compliance API transcript pagination

This .NET 10 console sample demonstrates a safe walk over the Anthropic Compliance API's local-session transcript endpoint. A transcript page may contain fewer messages than the requested `limit` because the response reached its size limit. The walk is complete only when `next_page` is `null`.

The verifier is entirely offline. A fake `HttpMessageHandler` returns deterministic JSON fixtures, so no Anthropic account, credential, network request, or paid model call is needed.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

## Setup and run

From this folder, restore and build the project:

```powershell
dotnet restore
dotnet format --verify-no-changes
dotnet build --no-restore --configuration Release
```

Run the deterministic checks:

```powershell
dotnet run --no-build --configuration Release
```

Expected output:

```text
PASS a short page follows next_page until null
PASS the returned message order is preserved
PASS a repeated cursor fails closed
PASS an invalid cursor response remains visible
4 deterministic checks passed.
```

## What the sample verifies

`ComplianceTranscriptPager` requests ascending order and keeps appending messages in the order returned by the API. Its first fixture contains two messages even though `limit=3`, plus a non-null cursor. The second fixture contains the final message and a null cursor. This catches the tempting but incorrect `data.Count < limit` stopping rule.

The fixtures also verify these defensive behaviors:

- `page` is omitted on the first request and populated from `next_page` afterward.
- Timestamps are not used to re-sort messages. Local transcript messages reconstructed from the same inference call share a timestamp.
- A repeated non-null cursor fails instead of creating an infinite loop.
- An HTTP 400 remains visible to the caller. In a live exporter, an expired cursor should cause a fresh walk without the `page` parameter, not a retry with the same cursor.

## Adapting it to the live API

The sample targets the local-session route:

```text
GET /v1/compliance/apps/sessions/local/{session_id}/messages?order=asc&limit=1000
```

For a live integration, give `HttpClient` the `https://api.anthropic.com/` base address and add your Compliance Access Key from a secret store. Use a placeholder while wiring configuration; never commit the real value:

```powershell
$env:ANTHROPIC_COMPLIANCE_ACCESS_KEY = '<read from your secret store>'
```

The session endpoints are available only to Claude Enterprise organizations and require a Compliance Access Key with `read:compliance_user_data`. Cowork and Claude Code session endpoints are stable. Claude Science and Claude for Microsoft 365 transcript coverage is beta.

## Limitations

This sample does not call Anthropic, implement authentication, retry rate limits, store checkpoints, or handle transcript content. Real transcript data can be sensitive. Content blocks can be truncated. An unavailable local message has an empty `content` array and `provenance.type` set to `content_unavailable`, so an exporter must preserve those signals rather than treating partial content as complete.

Transcript cursors are bound to the session and sort order, and a walk's cursors expire 24 hours after the first page. Complete a walk promptly. If the API returns 400 for an expired or mismatched cursor, discard that cursor and restart from the first page, accepting that the current retention boundary may differ.

## References

- [Anthropic: Retrieve session transcripts](https://platform.claude.com/docs/en/manage-claude/compliance-sessions)
- [Anthropic: Claude Platform release notes](https://platform.claude.com/docs/en/release-notes/overview)
