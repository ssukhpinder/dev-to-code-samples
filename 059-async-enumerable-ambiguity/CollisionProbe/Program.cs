using System.Runtime.CompilerServices;

var projected = Values().Select(static value => value * 2);

await foreach (var value in projected)
{
    Console.WriteLine(value);
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
