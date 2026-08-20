# .NET 10 UTF-8 hex conversion

## Problem

Protocol parsers often receive hexadecimal identifiers as UTF-8 bytes. Converting those bytes to a temporary `string` before parsing adds an avoidable representation change, and a happy-path parser can miss malformed input or undersized buffers.

This sample uses the stable .NET 10 UTF-8 overloads on `Convert` to parse and write a fixed-width trace ID directly between byte spans. It checks the returned `OperationStatus`, validates the protocol's 32-byte wire length, and treats buffer sizing as part of the contract.

## Prerequisites

- .NET 10 SDK
- No packages, credentials, network services, or environment variables

The APIs are documented in [What's new in .NET 10 libraries](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries#utf-8-support-for-hex-string-conversion), [`Convert.FromHexString`](https://learn.microsoft.com/dotnet/api/system.convert.fromhexstring?view=net-10.0), and [`Convert.TryToHexStringLower`](https://learn.microsoft.com/dotnet/api/system.convert.trytohexstringlower?view=net-10.0).

## Setup and verification

From this folder, run:

```powershell
dotnet restore
dotnet format Utf8HexConversion.csproj --verify-no-changes --no-restore
dotnet build Utf8HexConversion.csproj -c Release --no-restore
dotnet run --project Utf8HexConversion.csproj -c Release --no-build
dotnet list Utf8HexConversion.csproj package --include-transitive
dotnet list Utf8HexConversion.csproj package --vulnerable --include-transitive
```

The console program is the deterministic verifier. A successful run prints:

```text
PASS valid lowercase trace ID decoded
PASS decoder reported exact consumption and output
PASS decoded bytes matched the fixture
PASS lowercase encoder filled the bounded destination
PASS lowercase round trip matched the wire ID
PASS uppercase input decoded to the same bytes
PASS odd-length input returned NeedMoreData
PASS non-hex input returned InvalidData
PASS undersized decode destination was rejected
PASS undersized encode destination was rejected
PASS protocol length guard rejected a short ID
11/11 checks passed.
```

The fixtures are constants. The verifier does not use the current time, random data, the current culture, files, or external calls.

## What to carry into a real parser

`Convert.FromHexString(ReadOnlySpan<byte>, Span<byte>, ...)` returns an `OperationStatus` plus the consumed and written counts. Accept the value only when the status is `Done` and the counts match the protocol field. `InvalidData` covers a non-hex byte, `NeedMoreData` identifies an incomplete final byte pair, and `DestinationTooSmall` means the caller did not provide enough output space.

`Convert.TryToHexStringLower` follows the usual try-pattern for encoding. A 16-byte trace ID needs exactly 32 destination bytes. Its lowercase output is useful for canonical wire formatting, while the decoder accepts uppercase input too.

The runtime validates hexadecimal syntax; the small `TraceIdCodec` wrapper validates application semantics. It rejects a shorter, otherwise valid hex value because this protocol requires exactly 16 decoded bytes.

## Limitations

This is a bounded-codec demonstration, not a complete W3C Trace Context validator. It does not parse a `traceparent` header, reject all-zero trace IDs, authenticate an identifier, or benchmark allocations. Use the protocol's full validation rules around this conversion primitive.
