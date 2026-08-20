# .NET 10 PEM UTF-8 scanner

## Problem

PEM files commonly arrive as bytes, but converting the entire input to a `string` just to locate one or more PEM blocks creates an avoidable detour. .NET 10 adds `PemEncoding.FindUtf8(ReadOnlySpan<byte>)`, which returns byte ranges for the block, label, and Base64 payload.

This sample scans two blocks directly from a byte fixture, decodes each Base64 payload, keeps absolute byte offsets, and demonstrates an important boundary: `FindUtf8` does not validate UTF-8 outside the encapsulation boundaries.

## Prerequisites

- .NET 10 SDK (validated with SDK 10.0.303 and runtime 10.0.11)
- No NuGet packages, credentials, certificate store, or external service

## Setup and validation

Run these commands from this folder:

```powershell
dotnet restore
dotnet format PemUtf8Scanner.csproj --verify-no-changes --no-restore
dotnet build PemUtf8Scanner.csproj -c Release --no-restore
dotnet run --project PemUtf8Scanner.csproj -c Release --no-build
dotnet list PemUtf8Scanner.csproj package --include-transitive
dotnet list PemUtf8Scanner.csproj package --vulnerable --include-transitive
```

The executable is also the deterministic verifier. It exits with a nonzero code when a check fails.

## Expected behavior

The verifier reports nine passing checks. It finds `CONFIG` and `METADATA`, decodes their fixed payloads, preserves increasing byte offsets, proves the leading `0xFF` byte is outside `FindUtf8`'s validation scope, applies a separate strict UTF-8 check, and rejects mismatched PEM labels.

The final lines include:

```text
BLOCK CONFIG offset=11 bytes=4 hex=01020304
BLOCK METADATA offset=79 bytes=5 hex=68656C6C6F
VERIFIED 9/9
```

Repeated runs produce byte-identical output because the fixture has fixed bytes and the sample uses no clock, randomness, locale-sensitive formatting, filesystem input, or network access.

## Adapting the sample

Replace the fixed fixture with bytes loaded from a placeholder such as `<path-to-pem-file>`. If the whole file must be valid UTF-8, run a strict decoder check before trusting surrounding text. Treat PEM discovery as framing only: parse the decoded bytes with the format-specific API and perform certificate, key, signature, or trust validation separately.

## Limitations

- The scanner locates syntactically matching PEM boundaries; it does not establish what the decoded bytes mean or whether they are trustworthy.
- `FindUtf8` uses the documented lax PEM rules and ignores invalid UTF-8 outside encapsulation boundaries.
- The loop stops when no additional valid block can be found. It is not a diagnostic parser for every malformed byte between blocks.
- The fixture contains public, non-secret bytes. No credential placeholder is needed to run it.
