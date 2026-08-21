# .NET 10 JSON console message parsing

## Problem

.NET 10 removes the redundant `State.Message` value from typical JSON console
log records. The formatted message remains at the top-level `Message` property,
while structured values and `{OriginalFormat}` remain under `State`.

A parser or snapshot test that reads only `State.Message` can therefore stop
finding messages after a runtime upgrade even though logging still succeeds.
This sample reproduces that contract change and verifies a parser that accepts
both the legacy and current shapes.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- No NuGet packages, credentials, accounts, paid services, or runtime network
  calls are required. Restore and vulnerability-audit commands may contact
  configured NuGet sources.

## Setup and validation

From this folder, run:

```powershell
dotnet restore JsonConsoleMessage.slnx
dotnet format JsonConsoleMessage.slnx --verify-no-changes --no-restore
dotnet build JsonConsoleMessage.slnx -c Release --no-restore
dotnet run --project Verifier/Verifier.csproj -c Release --no-build
dotnet list JsonConsoleMessage.slnx package --vulnerable --include-transitive
```

To inspect a real .NET 10 JSON log record directly:

```powershell
dotnet run --project Emitter/Emitter.csproj -c Release --no-build
```

The verifier exits nonzero on the first failed contract.

## Expected behavior

The run ends with deterministic semantic checks:

```text
PASS: legacy fixture has a top-level Message
PASS: legacy fixture duplicates Message in State
PASS: legacy-only parser reads the legacy fixture
PASS: .NET 10 emits the top-level Message
PASS: .NET 10 omits the duplicate State.Message
PASS: legacy-only parser misses the .NET 10 message
PASS: compatible parser reads the .NET 10 message
PASS: compatible parser still reads the legacy fixture
PASS: structured OrderId remains in State
PASS: structured Status remains in State
PASS: original format remains in State
PASS: 11/11 checks
```

The compatibility rule is deliberately small:

```csharp
static string? ReadCompatible(JsonElement root) =>
    ReadTopLevelMessage(root) ?? ReadStateMessage(root);
```

It prefers the current top-level field and uses `State.Message` only as a
legacy fallback. The verifier parses a documented legacy fixture and an actual
record produced by `AddJsonConsole` on the installed .NET 10 runtime. It checks
meaningful fields rather than timestamp text or JSON property order.

## Limitations

- Microsoft notes that `State.Message` can still appear when its value differs
  from the top-level message. Treat the top-level field as canonical rather
  than asserting that the nested field is universally forbidden.
- This sample verifies the built-in JSON console formatter. Other providers and
  collectors have their own schemas and compatibility policies.
- For new telemetry pipelines, prefer a structured transport or collector
  contract over scraping console output when one is available.

See Microsoft's [.NET 10 breaking-change note](https://learn.microsoft.com/dotnet/core/compatibility/extensions/10.0/console-json-logging-duplicate-messages)
and [JSON console formatter documentation](https://learn.microsoft.com/dotnet/core/extensions/console-log-formatter#json)
for the official behavior and configuration guidance.
