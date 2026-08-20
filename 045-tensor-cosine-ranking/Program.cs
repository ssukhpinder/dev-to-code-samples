using System.Numerics.Tensors;

var verifier = new Verifier();

float[] query = [1f, 0f, 1f, 0f];
EmbeddingDocument[] documents =
[
    new("css-grid", [0f, 1f, 0f, 1f]),
    new("cache-invalidation", [0.9f, 0.1f, 0.8f, 0f]),
    new("opposite-intent", [-1f, 0f, -1f, 0f]),
    new("auth-middleware", [0.8f, 0.2f, 0.7f, 0f]),
];

IReadOnlyList<RankedDocument> ranked = EmbeddingRanker.Rank(query, documents);

string[] expectedOrder =
[
    "cache-invalidation",
    "auth-middleware",
    "css-grid",
    "opposite-intent",
];

verifier.Check(
    ranked.Select(result => result.Id).SequenceEqual(expectedOrder),
    "fixed documents ranked in the expected order");
verifier.Check(
    ranked.Zip(ranked.Skip(1), (left, right) => left.Score >= right.Score).All(result => result),
    "scores are descending");
verifier.Check(
    ranked.All(result => float.IsFinite(result.Score) && result.Score is >= -1.0001f and <= 1.0001f),
    "scores are finite and within cosine tolerance");

EmbeddingDocument[] tiedDocuments =
[
    new("doc-b", [1f, 0f, 1f, 0f]),
    new("doc-a", [2f, 0f, 2f, 0f]),
];
IReadOnlyList<RankedDocument> tied = EmbeddingRanker.Rank(query, tiedDocuments);

verifier.Check(
    tied.Select(result => result.Id).SequenceEqual(["doc-a", "doc-b"]),
    "ordinal document ID breaks equal-score ties");
verifier.Check(
    tied.All(result => MathF.Abs(result.Score - 1f) < 0.0001f),
    "parallel vectors score approximately one");

verifier.Check(
    Throws<ArgumentException>(() =>
        EmbeddingRanker.Rank(query, [new("wrong-dimension", [1f, 0f, 1f])])),
    "dimension mismatch rejected");
verifier.Check(
    Throws<ArgumentException>(() =>
        EmbeddingRanker.Rank(query, [new("zero-vector", [0f, 0f, 0f, 0f])])),
    "zero vector rejected");
verifier.Check(
    Throws<ArgumentException>(() =>
        EmbeddingRanker.Rank(query, [new("not-a-number", [1f, float.NaN, 0f, 0f])])),
    "NaN input rejected");
verifier.Check(
    Throws<ArgumentException>(() =>
        EmbeddingRanker.Rank([1f, float.PositiveInfinity], [new("finite", [1f, 0f])])),
    "infinite input rejected");
verifier.Check(
    Throws<ArgumentException>(() => EmbeddingRanker.Rank([], documents)),
    "empty query rejected");

verifier.Complete();

static bool Throws<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
        return false;
    }
    catch (TException)
    {
        return true;
    }
}

internal sealed record EmbeddingDocument(string Id, float[] Vector);

internal sealed record RankedDocument(string Id, float Score);

internal static class EmbeddingRanker
{
    public static IReadOnlyList<RankedDocument> Rank(
        ReadOnlySpan<float> query,
        IReadOnlyList<EmbeddingDocument> documents)
    {
        ValidateVector(query, "query");

        var results = new List<RankedDocument>(documents.Count);

        foreach (EmbeddingDocument document in documents)
        {
            if (document.Vector.Length != query.Length)
            {
                throw new ArgumentException(
                    $"Document '{document.Id}' has {document.Vector.Length} dimensions; expected {query.Length}.",
                    nameof(documents));
            }

            ValidateVector(document.Vector, $"document '{document.Id}'");

            float score = TensorPrimitives.CosineSimilarity(query, document.Vector);
            if (!float.IsFinite(score))
            {
                throw new InvalidOperationException(
                    $"Cosine similarity for document '{document.Id}' was not finite.");
            }

            results.Add(new RankedDocument(document.Id, score));
        }

        return results
            .OrderByDescending(result => result.Score)
            .ThenBy(result => result.Id, StringComparer.Ordinal)
            .ToArray();
    }

    private static void ValidateVector(ReadOnlySpan<float> vector, string name)
    {
        if (vector.IsEmpty)
        {
            throw new ArgumentException($"The {name} vector must not be empty.");
        }

        bool hasNonZeroValue = false;

        foreach (float value in vector)
        {
            if (!float.IsFinite(value))
            {
                throw new ArgumentException($"The {name} vector contains NaN or infinity.");
            }

            hasNonZeroValue |= value != 0f;
        }

        if (!hasNonZeroValue)
        {
            throw new ArgumentException($"The {name} vector must not be all zeros.");
        }
    }
}

internal sealed class Verifier
{
    private int _passed;
    private int _failed;

    public void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS {name}");
            return;
        }

        _failed++;
        Console.WriteLine($"FAIL {name}");
    }

    public void Complete()
    {
        Console.WriteLine($"{_passed}/{_passed + _failed} checks passed.");

        if (_failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
