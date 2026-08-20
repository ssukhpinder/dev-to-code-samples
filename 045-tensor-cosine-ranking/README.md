# .NET 10 TensorPrimitives cosine similarity

## Problem

An offline embedding-ranking test is useful before a RAG pipeline reaches a model or vector database. A raw cosine-similarity call is not enough: mismatched dimensions, empty inputs, zero vectors, and non-finite values can turn a small ranking fixture into an exception or a `NaN` score.

This sample uses the stable `TensorPrimitives.CosineSimilarity` API in .NET 10 to rank fixed embedding-like vectors. A small guard layer validates the inputs, and an ordinal document ID provides a deterministic tie-break when scores are equal.

## Prerequisites

- .NET 10 SDK
- `System.Numerics.Tensors` 10.0.11, restored from NuGet
- No credentials, model calls, vector database, network service, environment variables, or secret placeholders are required

Microsoft documents the tensor APIs as stable for .NET 10 in [What's new in .NET 10 libraries](https://learn.microsoft.com/dotnet/core/whats-new/dotnet-10/libraries#tensor-enhancements). The [`TensorPrimitives.CosineSimilarity`](https://learn.microsoft.com/dotnet/api/system.numerics.tensors.tensorprimitives.cosinesimilarity?view=net-10.0-pp) reference describes its equal-length, non-empty input contract and floating-point behavior.

## Setup and verification

From this folder, run:

```powershell
dotnet restore
dotnet format TensorCosineRanking.csproj --verify-no-changes --no-restore
dotnet build TensorCosineRanking.csproj -c Release --no-restore
dotnet run --project TensorCosineRanking.csproj -c Release --no-build
dotnet list TensorCosineRanking.csproj package --include-transitive
dotnet list TensorCosineRanking.csproj package --vulnerable --include-transitive
```

The console program is the deterministic test harness. A successful run prints:

```text
PASS fixed documents ranked in the expected order
PASS scores are descending
PASS scores are finite and within cosine tolerance
PASS ordinal document ID breaks equal-score ties
PASS parallel vectors score approximately one
PASS dimension mismatch rejected
PASS zero vector rejected
PASS NaN input rejected
PASS infinite input rejected
PASS empty query rejected
10/10 checks passed.
```

The fixtures are constants. The verifier does not use current time, randomness, files, credentials, a model, a database, or any runtime network call.

## What the guard layer adds

`TensorPrimitives.CosineSimilarity` computes the score; the application still owns the embedding contract. `EmbeddingRanker` checks that every document has the query's dimension, rejects empty or all-zero vectors, and rejects `NaN` or infinity before scoring. It also rejects a non-finite result instead of letting one contaminate sort order.

The sample sorts by score descending and then by document ID with `StringComparer.Ordinal`. That second key matters in regression tests because equally directed vectors can produce equal cosine scores even when their magnitudes differ.

Use tolerance-based assertions for floating-point scores. The API may use architecture-specific instructions, so exact last-bit results can differ across operating systems or processors even when the ranking is correct.

## Limitations

This is a brute-force, in-memory preflight, not an approximate-nearest-neighbor index or a vector store. The hand-written fixtures prove ranking mechanics, not semantic retrieval quality. Real embeddings must come from the same model and use the same dimension, preprocessing, and distance convention. The sample also does not benchmark throughput or claim that cosine similarity is the right metric for every embedding model.
