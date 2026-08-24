# .NET 10 BufferedStream explicit flush

## Problem

In .NET 9 and earlier, the `BufferedStream.WriteByte` call that filled the internal buffer also flushed the underlying stream. .NET 10 removed that implicit flush so `WriteByte` behaves consistently with the other buffered write APIs.

Code that treated “buffer full” as a flush side-effect or protocol boundary can therefore observe different behavior after an upgrade. The buffered bytes can still be written to the underlying stream to make room; the removed behavior is the accompanying `Flush()` call. This sample makes that distinction visible with a deterministic in-memory tracking stream, then proves that an explicit `Flush()` works on both target frameworks.

## Prerequisites

- .NET SDK 10.0.303 or a later supported .NET 10 SDK.
- .NET 9 and .NET 10 runtimes. The verified versions are 9.0.18 and 10.0.11.

No credentials, accounts, paid services, external storage, or network calls are required. There are no credential placeholders to configure.

## Setup

Run the commands from this folder:

```powershell
dotnet restore .\BufferedStreamFlushDemo.csproj
```

The project has no package dependencies.

## Run

Run the same verifier on both frameworks:

```powershell
dotnet run --project .\BufferedStreamFlushDemo.csproj -c Release -f net9.0
dotnet run --project .\BufferedStreamFlushDemo.csproj -c Release -f net10.0
```

The verifier writes exactly four bytes into a four-byte `BufferedStream`, snapshots the underlying stream, calls `Flush()`, and takes a second snapshot.

## Expected behavior

Before the explicit call, the target frameworks intentionally differ:

| Target | Underlying flush calls | Visible bytes |
|---|---:|---|
| `net9.0` | 1 | `010203` (the fourth byte remains buffered) |
| `net10.0` | 0 | `010203` (the fourth byte remains buffered) |

After `Flush()`, both targets expose `01020304` in the original order. The explicit call increments the underlying flush count exactly once, and the verifier reports `7/7 passed`.

## Deterministic verification

```powershell
dotnet restore .\BufferedStreamFlushDemo.csproj
dotnet format .\BufferedStreamFlushDemo.csproj --verify-no-changes --no-restore
dotnet build .\BufferedStreamFlushDemo.csproj -c Release --no-restore
dotnet run --project .\BufferedStreamFlushDemo.csproj -c Release -f net9.0 --no-build
dotnet run --project .\BufferedStreamFlushDemo.csproj -c Release -f net10.0 --no-build
dotnet list .\BufferedStreamFlushDemo.csproj package --include-transitive
dotnet list .\BufferedStreamFlushDemo.csproj package --vulnerable --include-transitive
```

For an additional repeatability check:

```powershell
1..5 | ForEach-Object {
    dotnet run --project .\BufferedStreamFlushDemo.csproj -c Release -f net10.0 --no-build
    if ($LASTEXITCODE -ne 0) { throw "Verifier failed on run $_" }
}
```

The verifier uses fixed bytes, no clock, no randomness, no locale-sensitive formatting, and no filesystem or network fixture.

## Files

- `BufferedStreamFlushDemo.csproj` multi-targets `net9.0` and `net10.0` with warnings treated as errors.
- `Program.cs` contains the tracking stream, snapshots, and seven executable contracts.

## Limitations

This sample observes managed `Stream.Write` and `Stream.Flush` calls. It does not claim that `Flush()` commits data to physical media or makes a remote protocol durable. Choose a durability boundary that matches the actual underlying stream and protocol.
