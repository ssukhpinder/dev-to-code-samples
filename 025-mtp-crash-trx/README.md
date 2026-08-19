# Keep TRX evidence when an MTP test host crashes

A hard test-host crash used to leave me with a failed job and little evidence
about which test was running. Microsoft.Testing.Platform (MTP) 2.3 streams TRX
results while the run is active and can record a crash sequence beside a dump.
That combination preserves completed results and identifies the in-flight test.

This .NET 10 sample uses MSTest.Sdk 4.3.3 and its stable MTP 2.3.x dependencies.
The normal run passes three tests. A second, explicitly guarded run sets
`DEMO_CRASH=1`, lets the first test finish, and calls `Environment.FailFast` from
the second test. The verifier then proves that:

- the completed first result remains in the streamed TRX;
- the third test never starts;
- the crash sequence names the second test as in flight; and
- dump collection is reported when the host can produce one.

## Prerequisites

- .NET 10 SDK
- PowerShell 7 or Windows PowerShell 5.1
- Network access for the initial NuGet restore
- Enough local disk space for a Mini dump

No API keys, model calls, paid services, containers, or external test systems are
required. The committed `global.json` selects the Microsoft.Testing.Platform
runner and pins MSTest.Sdk 4.3.3. `testconfig.json` disables parallel execution
and orders the three methods by name so the evidence is deterministic.

## Setup and normal run

From this folder:

```powershell
dotnet restore MtpCrashTrx.csproj
dotnet build MtpCrashTrx.csproj --configuration Release --no-restore
dotnet run --project MtpCrashTrx.csproj --configuration Release --no-build -- `
  --report-trx `
  --report-trx-filename normal.trx `
  --results-directory TestResults/normal
```

The normal run exits with code `0`, reports three passed tests, and writes
`TestResults/normal/normal.trx`.

## Run the controlled crash verification

Do not set `DEMO_CRASH` globally. The verifier scopes it to the child process,
expects that process to fail, and restores the previous value afterward:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
```

With PowerShell 7 on any supported platform, use `pwsh -File ./verify.ps1`.

Internally, the crash run passes the stable MTP options directly to the test
application:

```text
--report-trx --report-trx-filename crash.trx
--crashdump --crashdump-type Mini --crash-sequence on
```

Expected final verifier output resembles:

```text
PASS normal TRX: 3/3 tests passed
PASS crash exit: <non-zero platform-specific value>
PASS streamed crash TRX retained A_CompletedBeforeCrash
PASS crash sequence identified B_CrashHostOnlyWhenRequested
PASS crash dump: <platform-specific filename> (<non-zero size> bytes)
# or: INFO crash dump unavailable on this host; inspect crash.console.log
```

The dump filename, size, availability, and crash exit code vary by operating
system, permissions, and runtime. The verifier requires the streamed TRX and
crash sequence; it treats a dump as additional evidence because dump collection
can fail even when those smaller artifacts survive. Inspect `TestResults/crash`
afterward; the ignored folder also contains `crash.console.log`.

## Validate the project

```powershell
dotnet format MtpCrashTrx.csproj --verify-no-changes --no-restore
dotnet build MtpCrashTrx.csproj --configuration Release --no-restore
dotnet run --project MtpCrashTrx.csproj --configuration Release --no-build
powershell -NoProfile -ExecutionPolicy Bypass -File .\verify.ps1
dotnet list MtpCrashTrx.csproj package --vulnerable --include-transitive
```

## Limitations and when not to use it

`Environment.FailFast` is intentionally destructive to the test process. Keep the
environment-variable guard, run it only in an isolated test host, and never add
this demonstration to an ordinary unguarded test command. A Mini dump is useful
for process state but contains less memory than Heap or Full dumps. Dumps can also
contain sensitive application data, so production CI needs restricted storage,
retention, and access controls.

The crash-sequence option requires MTP 2.3 or later, and crash dumps require .NET
6 or later. The dump mechanism is not supported for .NET Framework and can still
fail because of host permissions or platform tooling. TRX preserves results
completed before the crash; it cannot invent a result for the test that
terminated the process.
