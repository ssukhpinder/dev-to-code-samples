# .NET 10 ValidationContext AOT-safe constructor

## Problem

The familiar `new ValidationContext(instance)` call is annotated with
`RequiresUnreferencedCode`. Enabling Native AOT analysis therefore reports
`IL2026` because that overload can use reflection to discover a display name.
Suppressing the warning hides the uncertainty rather than removing it.

.NET 10 adds a four-argument constructor that accepts the display name
explicitly. This sample makes the legacy call fail under warnings-as-errors,
then verifies that the new constructor builds without `IL2026` and preserves
the context data needed by known-property validation.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- A Native AOT toolchain is required only for the optional native publish.
- No explicit application package, credential, account, paid service, database,
  or runtime network call is required. The SDK restores its Native AOT compiler
  and linker packs from configured NuGet sources, and the audit may contact the
  same sources.

## Setup and validation

From this folder, run:

```powershell
dotnet restore ValidationContextAot.slnx
dotnet restore LegacyProbe/LegacyProbe.csproj
dotnet format ValidationContextAot.slnx --verify-no-changes --no-restore
dotnet build ValidationContextAot.slnx -c Release --no-restore
dotnet run --project Verifier/Verifier.csproj -c Release --no-build
dotnet list ValidationContextAot.slnx package --vulnerable --include-transitive
```

The verifier builds the legacy probe and expects it to fail with `IL2026`. It
then rebuilds and runs the fixed project, exiting nonzero on the first broken
contract. To prove the final app also compiles to native code, publish for the
current platform after installing its Native AOT prerequisites:

```powershell
dotnet publish AotSafeValidation/AotSafeValidation.csproj -c Release -r win-x64 --self-contained true --no-restore
```

Replace `win-x64` with the runtime identifier for the target platform.

## Expected behavior

The verifier ends with:

```text
PASS: legacy build fails under warnings-as-errors
PASS: legacy diagnostic is IL2026
PASS: legacy diagnostic names the one-argument constructor
PASS: AOT-aware build succeeds
PASS: AOT-aware build contains no IL2026
PASS: runtime contract passes 7/7 checks
PASS: 6/6 checks
```

The fixed call supplies the value that the old overload may discover through
reflection:

```csharp
var context = new ValidationContext(
    options,
    displayName: "Checkout customer",
    serviceProvider: services,
    items: sourceItems);
```

The runtime checks also prove that `ObjectInstance`, `Items`, and
`IServiceProvider` still behave as expected. A `RequiredAttribute` is invoked
directly for a known property, and its error uses the explicit display name.

## Limitations

- This constructor fixes the reflection needed to infer `DisplayName`; it does
  not make every DataAnnotations workflow AOT-safe. Reflection-based object
  traversal such as `Validator.TryValidateObject` has its own trim warning.
- Use a stable, intentional display name. `nameof(OptionsType)` is useful for a
  code-facing name; a localized UI label should come from the same localization
  policy as the rest of the application.
- Native AOT binaries are platform-specific, and publishing requires the native
  compiler toolchain documented for the target operating system.

See Microsoft's [.NET 10 library update](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries),
[`ValidationContext` constructor reference](https://learn.microsoft.com/dotnet/api/system.componentmodel.dataannotations.validationcontext.-ctor?view=net-10.0),
and [Native AOT deployment guide](https://learn.microsoft.com/dotnet/core/deploying/native-aot/)
for the official API and publishing guidance.
