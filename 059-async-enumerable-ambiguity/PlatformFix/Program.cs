using System.Runtime.CompilerServices;

var passed = 0;

var doubled = await CollectAsync(Values().Select(static value => value * 2));
Check("built-in Select returns the expected values", doubled.SequenceEqual([2, 4, 6]));

using var cancellation = new CancellationTokenSource();
var selectorSawEnumerationToken = false;
var asynchronouslyProjected = Values().Select(
    async (int value, CancellationToken cancellationToken) =>
    {
        selectorSawEnumerationToken |= cancellationToken == cancellation.Token;
        await Task.Yield();
        return value + 10;
    });

var projected = await CollectAsync(
    asynchronouslyProjected,
    cancellation.Token);

Check("Select replaces the old SelectAwait call shape", projected.SequenceEqual([11, 12, 13]));
Check("the async selector receives the enumeration token", selectorSawEnumerationToken);

var firstTwo = await CollectAsync(Values().Take(2));
Check("other platform async LINQ operators compose", firstTwo.SequenceEqual([1, 2]));

var repeated = await CollectAsync(Values().Select(static value => value));
Check("a repeated enumeration is deterministic", repeated.SequenceEqual([1, 2, 3]));

Console.WriteLine($"PASS {passed}/5");

void Check(string name, bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {name}");
    }

    passed++;
    Console.WriteLine($"PASS: {name}");
}

static async Task<List<int>> CollectAsync(
    IAsyncEnumerable<int> source,
    CancellationToken cancellationToken = default)
{
    var values = new List<int>();

    await foreach (var value in source.WithCancellation(cancellationToken))
    {
        values.Add(value);
    }

    return values;
}

static async IAsyncEnumerable<int> Values(
    [EnumeratorCancellation] CancellationToken cancellationToken = default)
{
    foreach (var value in new[] { 1, 2, 3 })
    {
        cancellationToken.ThrowIfCancellationRequested();
        await Task.Yield();
        yield return value;
    }
}
