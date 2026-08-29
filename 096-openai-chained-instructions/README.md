# OpenAI Responses API chained instructions

## Problem

`previous_response_id` chains a new Responses API request to an earlier response, but the earlier request's `instructions` are not carried into the new request. An application that treats those instructions as standing policy can silently lose its tone, output, or tool-use constraints on turn two.

This dependency-free .NET 10 sample builds policy-bound request payloads and fails locally when a caller omits the instructions. It also prevents the mutually exclusive `previous_response_id` and `conversation` fields from being combined.

## Prerequisites

- .NET SDK 10.0.303 or a later supported .NET 10 SDK
- No OpenAI account, API key, model call, or network access at runtime

The sample uses only the .NET shared framework and fixed fixture IDs.

## Setup and run

```powershell
dotnet restore
dotnet run -c Release
```

The builder emits a first-turn payload, a chained payload that repeats the same instructions, and a chained payload that deliberately replaces them.

## Deterministic verification

Run the complete local validation sequence:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package
dotnet list package --vulnerable --include-transitive
```

The executable checks:

1. A first turn carries explicit instructions and `store: true`.
2. A chained turn repeats the policy beside `previous_response_id`.
3. A chained turn can replace the policy deliberately.
4. Blank instructions are rejected before transport.
5. Blank response IDs are rejected before transport.
6. `previous_response_id` and `conversation` cannot be combined.
7. Rebuilding the same turn produces byte-identical JSON.

Expected result:

```text
PASS first turn carries instructions
PASS chained turn repeats instructions
PASS chained turn can replace instructions deliberately
PASS blank instructions fail before transport
PASS blank response IDs fail before transport
PASS previous response and conversation are mutually exclusive
PASS payload key order and content stay deterministic
payload-sha256=2146C0E0DBCD8BE1319924CDD62474EF41FC4FB37AC8B3BDB66C9A71013B4E68
7/7 checks passed
```

## Credentials and adaptation

No credential is read by this sample. If you adapt the payload builder for a live call, provide `OPENAI_API_KEY=<your-key>` through your process environment or secret manager. Do not place a real key in source, JSON fixtures, command history, or committed `.env` files.

The builder is a focused application guard, not an OpenAI SDK replacement. It intentionally requires `instructions` on every request because this example assumes they contain application policy. The API field itself is optional. The sample does not send HTTP requests, prove model output, implement `store: false` history replay, or manage durable Conversation objects.

Official references:

- [Create a model response](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
- [Conversation state](https://developers.openai.com/api/docs/guides/conversation-state)
