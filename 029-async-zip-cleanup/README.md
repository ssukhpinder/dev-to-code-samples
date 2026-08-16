# .NET 10 async ZIP extraction cleanup

`ZipFile.ExtractToDirectoryAsync` supports cancellation in .NET 10, but a failure can leave
the destination partially extracted. This sample keeps incomplete files away from the live
path by extracting into a unique sibling directory, moving that directory into place only
after success, and deleting staging output on the failure path.

## Prerequisites

- .NET 10 SDK
- No external services, packages, or credentials

## Setup and run

```bash
dotnet restore AsyncZipCleanup.csproj
dotnet run --project AsyncZipCleanup.csproj --configuration Release
```

Run formatting, build, and the deterministic verifier separately with:

```bash
dotnet format AsyncZipCleanup.csproj --verify-no-changes --no-restore
dotnet build AsyncZipCleanup.csproj --configuration Release --no-restore
dotnet run --project AsyncZipCleanup.csproj --configuration Release --no-build
```

## Expected behavior

The console verifier uses only temporary local files. It checks five cases:

1. A real in-memory ZIP is extracted and moved to the requested destination.
2. An injected cancellation occurs after a partial file is written, and cleanup removes it.
3. A real corrupt ZIP fails without leaving a destination or staging directory.
4. A pre-existing destination and its contents are preserved.
5. A destination created after the initial check wins the race without being overwritten.

Expected output:

```text
PASS: successful extraction publishes only the complete directory
PASS: cancellation removes partial staging files
PASS: corrupt input leaves no destination or staging directory
PASS: an existing destination is rejected without modification
PASS: a destination created during extraction wins the race safely
All 5 checks passed.
```

## Limitations

The final cancellation check is the commit point. Once it passes, the synchronous
`Directory.Move` is allowed to finish even if cancellation arrives during that move. Staging
and destination paths must be on the same volume, and the move fails safely if another
process creates the destination first.

Cleanup is one best-effort recursive delete. Locks, access-control rules, file-system errors,
or process termination can still leave staging data behind. If deletion throws, the sample
reports both failures in an `AggregateException`; a service should also record the staging
path and reap abandoned directories. The built-in extraction API does not cap entry count or
total uncompressed size. For untrusted archives, inspect entries and enforce those limits
before extraction rather than treating this wrapper as ZIP bomb protection.

Article: _.NET 10 Async ZIP Extraction: Clean Up After Cancellation_ (draft)
