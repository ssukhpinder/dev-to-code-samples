# .NET 10 span Unicode normalization

## Problem

Unicode can represent the same text with different code-unit sequences. For example, `Caf\u00E9` contains one precomposed `é`, while `Cafe\u0301` contains `e` followed by a combining accent. They look alike but do not compare as equal with ordinal comparison, so unnormalized identifiers can create duplicate keys or lookup misses.

This package-free .NET 10 sample uses the new span overloads on `StringNormalizationExtensions` to normalize incoming `ReadOnlySpan<char>` data directly into a caller-provided buffer. Short input uses `stackalloc`; larger input uses `ArrayPool<char>`. Only the final canonical `string` needed by the key set is created.

## Prerequisites

- .NET 10 SDK
- No credentials, external services, database, paid API, or third-party package

## Setup

Run from this folder:

```text
dotnet restore UnicodeSpanNormalization.csproj
```

## Run the demonstration

```text
dotnet run --project UnicodeSpanNormalization.csproj -c Release
```

Expected behavior:

```text
Ordinal before normalization: False
Canonical values equal: True
First registration accepted: True
Equivalent spelling accepted: False
Short input used pooled buffer: False
Long input used pooled buffer: True

Run with --verify for deterministic checks.
```

`GetNormalizedLength` sizes the destination, and `TryNormalize` writes Form C text into it. The canonical set uses `StringComparer.OrdinalIgnoreCase` only after normalization, so canonically equivalent spellings reach the same key.

## Verify it

```text
dotnet format UnicodeSpanNormalization.csproj --verify-no-changes
dotnet build UnicodeSpanNormalization.csproj -c Release --no-restore
dotnet run --project UnicodeSpanNormalization.csproj -c Release --no-build -- --verify
dotnet list UnicodeSpanNormalization.csproj package --include-transitive
dotnet list UnicodeSpanNormalization.csproj package --vulnerable --include-transitive
```

The offline verifier checks six fixed behaviors:

1. The composed and decomposed inputs differ before normalization.
2. Form C produces the same ordinal key for both.
3. Short input stays on the stack-buffer path.
4. A canonical duplicate is rejected by the set.
5. Long input uses the pooled buffer and produces normalized output.
6. A Latin `a` and visually similar Cyrillic `а` remain distinct.

Successful verification ends with `PASS 6/6`. Any failed check throws and exits nonzero.

## Limitations

Unicode normalization is not a complete account or identifier policy. It does not detect visually confusable characters, apply locale-aware casing, enforce a script allowlist, or decide which symbols your domain permits. Form C preserves compatibility characters; Form KC intentionally folds some of them and can change identifier meaning.

Validate input length before allocating or renting a buffer, and decide how the application handles invalid Unicode. This sample creates one final `string` because `HashSet<string>` owns its keys; pipelines that can keep data as spans may avoid that allocation too.
