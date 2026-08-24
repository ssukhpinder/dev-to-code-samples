# .NET 10 XmlSerializer obsolete properties

## Problem

.NET 10 changed `XmlSerializer`: a property marked with warning-only `[Obsolete]` is serialized by default instead of being treated like `[XmlIgnore]`. That can add an element to an established XML contract after a runtime upgrade. A member marked `[Obsolete(..., true)]` instead prevents serializer creation.

This offline sample turns those rules into six deterministic checks. It also shows that `[XmlIgnore]` remains the explicit way to remove a member and that the process-wide `Switch.System.Xml.IgnoreObsoleteMembers` AppContext switch can temporarily restore the legacy exclusion behavior.

## Prerequisites

- .NET 10 SDK. The sample was verified with SDK 10.0.303 and runtime 10.0.11.
- No credentials, external services, packages, or network access are required at runtime.

## Setup and validation

From this folder, run:

```powershell
dotnet restore .\XmlSerializerObsolete.csproj
dotnet format .\XmlSerializerObsolete.csproj --verify-no-changes --no-restore
dotnet build .\XmlSerializerObsolete.csproj -c Release --no-restore
dotnet run --project .\XmlSerializerObsolete.csproj -c Release --no-build
dotnet list .\XmlSerializerObsolete.csproj package --include-transitive
dotnet list .\XmlSerializerObsolete.csproj package --vulnerable --include-transitive
```

Expected verifier output:

```text
Default elements: Id, LegacyCode
Legacy-switch elements: Id
PASS default includes warning-only obsolete member
PASS XmlIgnore remains explicit exclusion
PASS warning-only obsolete member round-trips
PASS IsError obsolete member blocks serializer creation
PASS legacy AppContext switch restores old exclusion
PASS legacy output keeps non-obsolete member
Verified 6/6
```

The process exits with code `0` only when all six checks pass. The XML is generated in memory with fixed input; the verifier does not depend on files, clocks, randomness, locale, or network calls.

## What the sample proves

- Warning-only `[Obsolete]` controls compiler guidance, not the .NET 10 wire shape.
- `[XmlIgnore]` is still the durable, contract-level exclusion.
- Obsolete members still deserialize when their XML elements are present.
- `[Obsolete(..., true)]` makes `XmlSerializer` creation fail.
- The legacy AppContext switch is a process-wide migration bridge, so it should be set before serializers are created and tested across every XML contract in the process.

## Limitations

The sample verifies `XmlSerializer` on .NET 10 only. It does not cover `DataContractSerializer`, ASP.NET Core formatter configuration, schema validation, trimming, or Native AOT. The AppContext switch restores broad legacy behavior; it is not a per-type policy and should not replace explicit `[XmlIgnore]` annotations or XML contract tests.
