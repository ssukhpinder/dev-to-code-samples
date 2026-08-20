# ASP.NET Core 10 PipeReader JSON converter

ASP.NET Core 10 uses the `PipeReader` overloads of `JsonSerializer.DeserializeAsync` for Minimal API request binding, MVC input formatters, and `HttpRequestJsonExtensions`. A custom `JsonConverter<T>` that reads only `Utf8JsonReader.ValueSpan` can therefore lose a JSON token that crosses pipe segments.

This sample sends the same fixed JSON document through two paths:

1. a contiguous `MemoryStream`, where the intentionally broken converter appears to work;
2. a chunked `PipeReader`, where `HasValueSequence` becomes `true` and the broken converter loses the value.

It then repeats the segmented read with a converter that calls `Utf8JsonReader.GetString()`, which handles both `ValueSpan` and `ValueSequence` and also decodes JSON escapes.

## Prerequisites

- .NET 10 SDK
- No credentials, database, network service, or paid API

The project uses only the installed .NET and ASP.NET Core shared frameworks. Package restore does not add third-party dependencies.

## Restore and run

From the repository root:

```powershell
dotnet restore .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj
dotnet run --project .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj -c Release --no-restore
```

On Bash-compatible shells, replace the backslashes with `/`.

Expected behavior:

```text
PASS stream keeps the converter token contiguous
PASS PipeReader exposes a segmented JSON token
PASS ValueSpan-only converter loses the segmented token
PASS sequence-aware converter preserves the segmented token
PASS GetString decodes JSON escapes
Observed: payload=12020 bytes, pipe-buffer=4096, source-chunk=257, broken-value=0 chars
Verifier: 5/5 passed
```

The verifier exits with code `1` if any contract fails.

## Format and build

```powershell
dotnet format .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj --verify-no-changes --no-restore
dotnet build .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj -c Release --no-restore
dotnet list .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj package --include-transitive
dotnet list .\041-pipereader-json-converter\PipeReaderJsonConverter.csproj package --vulnerable --include-transitive
```

## What to change in a real converter

For string tokens, prefer `reader.GetString()` unless the converter specifically needs raw UTF-8 bytes. For raw bytes, branch on `reader.HasValueSequence` and process `reader.ValueSequence` when it is `true`; only use `reader.ValueSpan` for contiguous tokens.

The `Microsoft.AspNetCore.UseStreamBasedJsonParsing` AppContext switch can temporarily restore stream-based parsing in .NET 10, but it is a migration aid rather than the long-term fix. Update the converter and keep a segmented-input regression test.

This small fixture only covers one string converter. Converters for numbers, encoded binary data, and nested tokens need their own semantic assertions, and performance-sensitive code should avoid copying a large `ReadOnlySequence<byte>` without measuring it.
