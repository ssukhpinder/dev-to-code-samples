# .NET 10.0.11 self-contained runtime patch verification

A self-contained .NET application carries its own runtime. Updating the runtime on a build
or host machine does not alter an artifact that was already published. This sample makes
the minimum runtime patch an artifact contract and fails when the published `.deps.json`
contains an older runtime pack.

## Prerequisites

- .NET SDK 10.0.303 or a newer stable .NET 10 feature band
- A runtime identifier supported by .NET 10, such as `win-x64` or `linux-x64`

No account, credential, paid service, database, or runtime network access is required.
Restore can contact configured NuGet sources to acquire the selected runtime pack.

## Setup and publish

Run the commands from this folder. These Windows commands publish a self-contained
`win-x64` artifact with the latest .NET 10 patch selected by the SDK:

```powershell
dotnet --version
dotnet restore PatchProbe/PatchProbe.csproj -r win-x64
dotnet restore RuntimePatchVerifier/RuntimePatchVerifier.csproj
dotnet publish PatchProbe/PatchProbe.csproj -c Release -r win-x64 --self-contained true --no-restore -o artifacts/win-x64
dotnet build RuntimePatchVerifier/RuntimePatchVerifier.csproj -c Release --no-restore
```

For Linux x64, replace `win-x64` with `linux-x64` and use `artifacts/linux-x64`.
The project sets `TargetLatestRuntimePatch=true` explicitly. Restore and publish use the
same RID so `--no-restore` cannot silently reuse assets restored under a different policy.

## Verify the artifact

The verifier reads the runtime target and the
`runtimepack.Microsoft.NETCore.App.Runtime.<rid>/<version>` entry from the published
`.deps.json`. It accepts 10.0.11 or a newer patch in the 10.0 family:

```powershell
dotnet run --project RuntimePatchVerifier/RuntimePatchVerifier.csproj -c Release --no-build -- artifacts/win-x64/PatchProbe.deps.json win-x64 10.0.11
artifacts/win-x64/PatchProbe.exe
```

Expected output on SDK 10.0.303 is:

```text
PASS: win-x64 contains .NET runtime 10.0.11 (minimum 10.0.11).
Runtime pack: runtimepack.Microsoft.NETCore.App.Runtime.win-x64/10.0.11
Framework: .NET 10.0.11
Environment.Version: 10.0.11
```

The first two lines validate the artifact without executing a target-RID binary. The last
two lines are an optional same-platform confirmation.

## Deterministic checks

The in-memory self-test covers the minimum patch, a newer patch, a stale patch, a wrong
RID, a framework-dependent shape, a prerelease version, malformed JSON, and an
unreferenced runtime pack:

```powershell
dotnet run --project RuntimePatchVerifier/RuntimePatchVerifier.csproj -c Release --no-build -- --self-test
dotnet format PatchProbe/PatchProbe.csproj --verify-no-changes --no-restore
dotnet format RuntimePatchVerifier/RuntimePatchVerifier.csproj --verify-no-changes --no-restore
```

Expected self-test result: `8/8 passed`.

## Limits

This check proves which managed runtime pack the publish artifact declares. It does not
prove that the same bytes reached a deployment target; use artifact hashes or provenance
for that boundary. Framework-dependent deployments select a host runtime when they run,
so this exact gate does not apply to them. Native AOT and some single-file layouts also
need checks tailored to their output format.

## References

- [.NET 10.0.11 release notes](https://github.com/dotnet/core/blob/main/release-notes/10.0/10.0.11/10.0.11.md)
- [Select which .NET version to use](https://learn.microsoft.com/dotnet/core/versions/selection#self-contained-deployments-include-the-selected-runtime)
- [Self-contained deployment runtime patch selection](https://learn.microsoft.com/dotnet/core/deploying/runtime-patch-selection)
