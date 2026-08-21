# Claude structured outputs refusal handling

## Problem

Claude structured outputs normally return JSON that matches the requested
schema, but the response envelope still controls whether that JSON is safe to
deserialize. A refusal is returned as HTTP 200 with `stop_reason: "refusal"`
and may not match the schema. A response stopped by `max_tokens` may contain
incomplete JSON. Enum or `const` text can also differ only in letter casing.

If an application deserializes `content[].text` before checking `stop_reason`,
it turns documented response states into misleading JSON errors. This sample
uses saved fixtures to prove a small decoder boundary that:

1. Classifies `refusal` and `max_tokens` before payload parsing.
2. Deserializes only a completed `end_turn` text block.
3. Accepts case-only enum differences against a fixed allowlist.
4. Fails closed for malformed envelopes, payloads, and unsupported stop
   reasons.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- No NuGet package dependencies, Claude API key, model call, or paid service.
  The compiled verifier runs offline; restore and vulnerability-audit commands
  can consult the configured NuGet sources.

## Setup and validation

From this folder, run:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package --vulnerable --include-transitive
```

The console application is the deterministic verifier. It exits nonzero on the
first contract failure.

## Expected behavior

The run ends with:

```text
PASS: completed response is accepted
PASS: completed payload is deserialized
PASS: HTTP 200 refusal is classified before payload parsing
PASS: refusal returns no domain value
PASS: max_tokens is classified before payload parsing
PASS: truncated response returns no domain value
PASS: enum casing variant is accepted
PASS: enum maps only to a declared value
PASS: completed malformed JSON is an invalid payload
PASS: unknown stop reason fails closed
PASS: missing text block is an invalid envelope
PASS: duplicate stop_reason is rejected
PASS: 12/12 checks
```

The fixtures use fixed IDs and content. The verifier prints no response text,
generated identifier, timestamp, random value, environment-dependent path, or
locale-dependent value, so repeated runs produce identical output.

## Decoder contract

The decoder treats the outer message and inner structured text as two separate
contracts. It parses the message envelope first and returns a typed status for
`refusal`, `max_tokens`, or an unsupported stop reason. Only `end_turn`
continues to text-block selection and JSON deserialization.

Case normalization is limited to the declared `approve` and `escalate` values.
It does not turn arbitrary strings into accepted business decisions. The
payload parser uses .NET's strict JSON options so missing, duplicate, or unknown
properties are rejected.

Anthropic's [structured outputs guide](https://platform.claude.com/docs/en/build-with-claude/structured-outputs)
documents the current `output_config.format` request shape, HTTP 200 refusals,
`max_tokens` truncation, and enum casing exception. The [stop-reason guide](https://platform.claude.com/docs/en/build-with-claude/handling-stop-reasons)
describes why successful responses must be routed by `stop_reason`.

## Limitations

- The fixtures are reduced, synthetic excerpts from completed, non-streaming
  Messages API response bodies. They retain only fields inspected by the
  decoder. Streaming clients must read `stop_reason` from the final message
  delta before applying the same classification.
- This decoder intentionally accepts only `end_turn` for a request expecting
  one text result. Tool use, custom stop sequences, and server-tool pauses need
  their own state-machine branches.
- Retry, fallback, logging, and user-facing refusal policy belong above the
  decoder. The sample does not automatically retry refusals or truncated
  responses.
- The sample verifies response handling, not remote schema enforcement or model
  behavior.
