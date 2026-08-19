# .NET 10 numeric string sorting

Lexicographic sorting puts `file10.txt` before `file9.txt`. This package-free .NET 10 sample uses `CompareOptions.NumericOrdering` to sort digit runs by numeric value, then verifies the less obvious equality and API constraints that come with that choice.

## Prerequisites

- .NET 10 SDK
- No credentials, external services, or third-party packages

## Setup

```bash
cd 038-numeric-string-sorting
dotnet restore
```

## Run the comparison

```bash
dotnet run -c Release
```

Expected behavior:

```text
Ordinal: file02.txt | file10.txt | file2.txt | file9.txt
Numeric: file2.txt | file02.txt | file9.txt | file10.txt
file2.txt equals file02.txt: True
v1.5 equals v1.05: True

Run with --verify for deterministic checks.
```

`StringComparer.Create` turns the culture and `CompareOptions.NumericOrdering` into one comparer that can be passed to LINQ and collections. The sample uses `InvariantCulture` so its output stays reproducible across machines. A user-interface list may instead need the user's current culture.

## Verify it

```bash
dotnet format NumericStringSorting.csproj --verify-no-changes
dotnet build NumericStringSorting.csproj -c Release --no-restore
dotnet run --project NumericStringSorting.csproj -c Release --no-build -- --verify
dotnet list NumericStringSorting.csproj package --vulnerable --include-transitive
```

The verifier checks all of these behaviors offline:

1. Ordinal sorting exposes the `file10`/`file9` ordering problem.
2. Numeric sorting puts `file9` before `file10`.
3. `file2.txt` and `file02.txt` compare as equal.
4. A `HashSet<string>` using the comparer keeps only one of those equal names.
5. A period ends one digit run and starts another, so `v1.5` and `v1.05` compare as equal rather than as decimal values.
6. Index-based operations reject `NumericOrdering`.

Successful verification ends with `PASS 6/6`.

## Limits

`NumericOrdering` is a culture-aware string collation option, not a parser for signed numbers, decimal values, semantic versions, or file-system identity. Punctuation such as `-`, `+`, and `.` terminates a digit run. Use a domain parser when those characters have numeric meaning.

Do not reuse this comparer for security decisions or as a dictionary/set equality comparer when leading-zero spellings must remain distinct. Use `StringComparer.Ordinal` or `StringComparer.OrdinalIgnoreCase` for protocol identifiers and other non-linguistic keys.
