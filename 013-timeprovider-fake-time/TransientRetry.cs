namespace TimeProviderTests;

/// <summary>
/// Small retry helper with exponential backoff. The only interesting part:
/// it takes a TimeProvider and passes it to Task.Delay, so a test can own the clock.
/// </summary>
public sealed class TransientRetry(TimeProvider clock, int maxAttempts, TimeSpan baseDelay)
{
    public int Attempts { get; private set; }

    public async Task<T> ExecuteAsync<T>(Func<Task<T>> operation, CancellationToken ct = default)
    {
        var delay = baseDelay;
        while (true)
        {
            Attempts++;
            try
            {
                return await operation().ConfigureAwait(false);
            }
            catch (Exception) when (Attempts < maxAttempts)
            {
                await Task.Delay(delay, clock, ct).ConfigureAwait(false);
                delay *= 2;
            }
        }
    }
}
