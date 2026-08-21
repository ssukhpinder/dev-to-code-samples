# C# 14 span overload expression trees

## Problem

C# 14 makes span conversions part of overload resolution. An expression such
as `(values, expected) => values.Contains(expected)` can therefore bind to
`MemoryExtensions.Contains(ReadOnlySpan<T>, T)` instead of
`Enumerable.Contains(IEnumerable<T>, T)`.

The expression still works when compiled to IL, but an expression-tree
interpreter cannot execute the span conversion. That can surface as a runtime
failure after a language-version upgrade even though the source did not
change.

This sample compiles the same expression under C# 13 and C# 14, inspects the
selected method, reproduces the interpreted failure, and verifies two explicit
ways to keep the expression interpreter-friendly.

## Prerequisites

- .NET 10 SDK. This sample was verified with SDK 10.0.303 and runtime 10.0.11.
- No NuGet packages, credentials, paid services, or runtime network calls are
  required. Restore and vulnerability-audit commands may contact configured
  NuGet sources.

## Setup and validation

From this folder, run:

```powershell
dotnet restore SpanExpressionTrees.slnx
dotnet format SpanExpressionTrees.slnx --verify-no-changes --no-restore
dotnet build SpanExpressionTrees.slnx -c Release --no-restore
dotnet run --project Verifier/Verifier.csproj -c Release --no-build
dotnet list SpanExpressionTrees.slnx package --vulnerable --include-transitive
```

The verifier exits nonzero on the first failed contract.

## Expected behavior

The run ends with deterministic output:

```text
PASS: C# 13 binds Enumerable.Contains
PASS: C# 14 binds MemoryExtensions.Contains
PASS: C# 13 expression runs with interpretation
PASS: C# 14 expression runs with IL compilation
PASS: C# 14 span expression rejects interpretation
PASS: explicit static call pins Enumerable.Contains
PASS: pinned expression runs with interpretation
PASS: IEnumerable cast pins Enumerable.Contains
PASS: cast expression runs with interpretation
PASS: pinned expression preserves false results
PASS: 10/10 checks
```

The two fixes are intentionally direct:

```csharp
(values, expected) => Enumerable.Contains(values, expected)
(values, expected) => ((IEnumerable<int>)values).Contains(expected)
```

Both make `Enumerable.Contains` part of the expression-tree contract instead
of relying on language-version-dependent extension-method selection.

## Limitations

- The failure is specific to expression interpretation. Normal IL compilation
  of this expression succeeds in the sample.
- Query translators may have a different failure mode when they encounter
  `MemoryExtensions.Contains`; inspect the generated tree and test the actual
  provider rather than assuming interpreter behavior.
- Pinning `Enumerable.Contains` favors a stable expression shape, not the span
  overload's direct-call performance characteristics.

See Microsoft's [C# 14 compatibility note](https://learn.microsoft.com/dotnet/core/compatibility/core-libraries/10.0/csharp-overload-resolution)
and the [first-class span feature specification](https://learn.microsoft.com/dotnet/csharp/language-reference/proposals/csharp-14.0/first-class-span-types)
for the binding rules.
