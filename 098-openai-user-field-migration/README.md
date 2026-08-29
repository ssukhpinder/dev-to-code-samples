# OpenAI Responses API user field migration

## Problem

The Responses API `user` field is deprecated. It previously mixed two concerns: identifying an end user for abuse detection and grouping related traffic for prompt caching. Replacing it with only one new field loses part of that intent.

This dependency-free .NET 10 sample builds an offline request payload with separate `safety_identifier` and `prompt_cache_key` values. It hashes the stable user subject with a secret pepper, keeps the cache group independent from user identity, and proves the deprecated field never reaches JSON.

## Prerequisites

- .NET SDK 10.0.303 or a later supported .NET 10 SDK
- No OpenAI account, API key, model call, or runtime network access
- For a real integration, at least 32 random pepper bytes stored in a secret manager

The committed pepper and identities are deterministic test fixtures, not credentials or production values.

## Setup and run

```powershell
dotnet restore
dotnet run -c Release
```

The executable prints fourteen `PASS` lines, the request JSON, a deterministic payload digest, and:

```text
14/14 checks passed
```

## Deterministic verification

Run the complete validation sequence from this folder:

```powershell
dotnet restore
dotnet format --verify-no-changes --no-restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
dotnet list package
dotnet list package --vulnerable --include-transitive
```

The verifier checks that:

1. `user` is absent while both replacement fields are present.
2. The safety identifier is a stable 64-character lowercase HMAC-SHA-256 digest.
3. Raw user identity never appears in the serialized request.
4. Different users get different safety identifiers.
5. Users sharing one reusable prompt prefix can share a cache key.
6. A prompt-version change produces a different cache key.
7. Length-prefixed cache components cannot collide at their boundary.
8. Unsupported characters and a cache key over 64 characters fail before transport.
9. A short privacy pepper fails before any transport boundary.
10. Repeated serialization is byte-identical.

## Credentials, adaptation, and limits

No credential is read and no HTTP request is made. For a live integration, provide the HMAC pepper through your platform's secret manager and provide `OPENAI_API_KEY=<your-key>` through the process environment or another protected credential source. Replace `your-model` at the application boundary. Never commit either secret or a real request containing user data.

The sample verifies request construction, not API acceptance, prompt-cache hits, model behavior, or abuse-detection outcomes. `prompt_cache_key` influences cache routing but does not guarantee a hit; the prompt prefix must still match. HMAC rotation also needs an overlap plan if a stable identifier must survive deployment changes.

Official references:

- [Create a model response](https://developers.openai.com/api/reference/cli/resources/responses/methods/create)
- [Safety best practices](https://developers.openai.com/api/docs/guides/safety-best-practices)
- [Prompt caching](https://developers.openai.com/api/docs/guides/prompt-caching)
