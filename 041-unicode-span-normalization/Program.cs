using System.Buffers;
using System.Text;

const string Composed = "Caf\u00E9";
const string Decomposed = "Cafe\u0301";

if (args.Contains("--verify", StringComparer.Ordinal))
{
    return Verify();
}

var composedKey = CanonicalKey.Create(Composed);
var decomposedKey = CanonicalKey.Create(Decomposed);
var registrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

Console.WriteLine($"Ordinal before normalization: {StringComparer.Ordinal.Equals(Composed, Decomposed)}");
Console.WriteLine($"Canonical values equal: {StringComparer.Ordinal.Equals(composedKey.Value, decomposedKey.Value)}");
Console.WriteLine($"First registration accepted: {registrations.Add(composedKey.Value)}");
Console.WriteLine($"Equivalent spelling accepted: {registrations.Add(decomposedKey.Value)}");
Console.WriteLine($"Short input used pooled buffer: {decomposedKey.UsedPooledBuffer}");

var longInput = string.Concat(Enumerable.Repeat(Decomposed, 80));
var longKey = CanonicalKey.Create(longInput);
Console.WriteLine($"Long input used pooled buffer: {longKey.UsedPooledBuffer}");
Console.WriteLine();
Console.WriteLine("Run with --verify for deterministic checks.");
return 0;

static int Verify()
{
    const string composed = "Caf\u00E9";
    const string decomposed = "Cafe\u0301";
    var passed = 0;

    Check(
        !StringComparer.Ordinal.Equals(composed, decomposed),
        "canonically equivalent input has different UTF-16 before normalization");

    var composedKey = CanonicalKey.Create(composed);
    var decomposedKey = CanonicalKey.Create(decomposed);

    Check(
        StringComparer.Ordinal.Equals(composedKey.Value, decomposedKey.Value),
        "Form C produces one ordinal key for both spellings");

    Check(
        !composedKey.UsedPooledBuffer && !decomposedKey.UsedPooledBuffer,
        "short input uses the stack buffer path");

    var registrations = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        composedKey.Value,
    };

    Check(
        !registrations.Add(decomposedKey.Value),
        "the canonical duplicate is rejected by the key set");

    var longInput = string.Concat(Enumerable.Repeat(decomposed, 80));
    var longKey = CanonicalKey.Create(longInput);

    Check(
        longKey.UsedPooledBuffer &&
        longKey.CharsWritten == 320 &&
        longKey.Value.AsSpan().IsNormalized(NormalizationForm.FormC),
        "long input uses the pooled path and remains normalized");

    var latin = CanonicalKey.Create("paypal");
    var cyrillic = CanonicalKey.Create("p\u0430ypal");

    Check(
        !StringComparer.Ordinal.Equals(latin.Value, cyrillic.Value),
        "normalization does not collapse visually confusable scripts");

    Console.WriteLine($"PASS {passed}/6");
    return 0;

    void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {description}");
        }

        passed++;
        Console.WriteLine($"PASS: {description}");
    }
}

internal static class CanonicalKey
{
    private const int StackBufferLimit = 256;

    public static CanonicalizationResult Create(ReadOnlySpan<char> source)
    {
        var normalizedLength = source.GetNormalizedLength(NormalizationForm.FormC);

        if (normalizedLength <= StackBufferLimit)
        {
            Span<char> destination = stackalloc char[normalizedLength];
            return NormalizeInto(source, destination, usedPooledBuffer: false);
        }

        var rented = ArrayPool<char>.Shared.Rent(normalizedLength);

        try
        {
            return NormalizeInto(
                source,
                rented.AsSpan(0, normalizedLength),
                usedPooledBuffer: true);
        }
        finally
        {
            ArrayPool<char>.Shared.Return(rented, clearArray: true);
        }
    }

    private static CanonicalizationResult NormalizeInto(
        ReadOnlySpan<char> source,
        Span<char> destination,
        bool usedPooledBuffer)
    {
        if (!source.TryNormalize(
                destination,
                out var charsWritten,
                NormalizationForm.FormC))
        {
            throw new InvalidOperationException("The normalization buffer was too small.");
        }

        return new CanonicalizationResult(
            new string(destination[..charsWritten]),
            usedPooledBuffer,
            charsWritten);
    }
}

internal readonly record struct CanonicalizationResult(
    string Value,
    bool UsedPooledBuffer,
    int CharsWritten);
