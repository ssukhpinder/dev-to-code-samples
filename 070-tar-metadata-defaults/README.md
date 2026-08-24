# .NET 10 TarEntry metadata defaults

This sample reproduces the .NET 10 change that stops new `GnuTarEntry` and `PaxTarEntry` instances from adding access-time (`atime`) and change-time (`ctime`) metadata by default. It also shows how to add fixed values explicitly and verify that they survive an in-memory TAR round trip.

## Problem

After moving a TAR-producing application or its tests from .NET 9 to .NET 10, code that expects every new GNU or PAX entry to contain `atime` and `ctime` can fail. In .NET 10, their omission is intentional. Most consumers do not need these fields, and some TAR readers do not support them.

The verifier runs the same source against both target frameworks:

- `net9.0` confirms the earlier constructor defaults.
- `net10.0` confirms the new omission behavior.
- Both targets write explicitly configured GNU and PAX entries, read them back from memory, and verify their timestamps and payloads.

## Prerequisites

- A stable .NET 10 SDK. `global.json` requests SDK `10.0.303` and allows a later patch in that feature band.
- The .NET 9 targeting pack and runtime for the comparison run.
- No credentials, accounts, external TAR tool, filesystem fixture, database, or network service.

## Setup

From this folder:

```bash
dotnet --info
dotnet restore TarMetadataDefaults.csproj
```

There are no NuGet package dependencies. The project uses `System.Formats.Tar` from the shared framework.

## Run and verify

Build both targets with warnings treated as errors:

```bash
dotnet build TarMetadataDefaults.csproj -c Release --no-restore
```

Run the .NET 9 comparison and the .NET 10 verifier:

```bash
dotnet run --project TarMetadataDefaults.csproj -c Release -f net9.0 --no-build
dotnet run --project TarMetadataDefaults.csproj -c Release -f net10.0 --no-build
```

Check formatting and dependency metadata:

```bash
dotnet format TarMetadataDefaults.csproj --verify-no-changes --no-restore
dotnet list TarMetadataDefaults.csproj package --include-transitive
dotnet list TarMetadataDefaults.csproj package --vulnerable --include-transitive
```

## Expected behavior

Each run reports `SUMMARY 14/14`. The .NET 9 run confirms that new entries receive `atime` and `ctime`; the .NET 10 run confirms that those defaults are absent. Both runs then prove that explicit GNU properties and explicit PAX extended attributes round-trip successfully.

The program pins the payload and every metadata value that it asserts. Its verification messages and results are deterministic and do not depend on the clock, locale, filesystem, or random input.

## Limitations

- The .NET 10 change does not remove `ModificationTime`; a newly constructed entry still initializes it from the current time unless the application sets it. This sample fixes it for reproducibility.
- Adding GNU `atime` and `ctime` can reduce compatibility with TAR readers that interpret the same header area differently. Add them only when a downstream contract requires them.
- PAX stores these timestamps as extended attributes. The sample validates numeric Unix seconds rather than depending on a particular decimal string representation.
- The verifier checks semantic round trips rather than promising byte-for-byte equality for PAX extended-header serialization.
- The sample uses in-memory entries. File-based `TarWriter.WriteEntry` overloads also consider source-file metadata and should have their own integration tests.
