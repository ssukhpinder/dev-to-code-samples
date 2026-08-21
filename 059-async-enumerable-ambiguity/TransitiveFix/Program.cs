using System.Runtime.CompilerServices;

var observed = new List<int>();

await foreach (var value in Values().Select(static value => value * 4))
{
    observed.Add(value);
}

if (!observed.SequenceEqual([4, 8, 12]))
{
    throw new InvalidOperationException("The ExcludeAssets mitigation changed the query result.");
}

Console.WriteLine("PASS: ExcludeAssets=compile keeps platform async LINQ unambiguous");
Console.WriteLine("PASS 1/1");

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
