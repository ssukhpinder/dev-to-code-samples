using System.Diagnostics;

namespace TimeProviderTests;

/// <summary>
/// The "before" picture: testing backoff against the real clock.
/// This test is correct, and it burns 7 real seconds every single run.
/// </summary>
public class RetrySleepingTests
{
    [Fact]
    public async Task Succeeds_after_three_transient_failures_real_clock()
    {
        var retry = new TransientRetry(TimeProvider.System, maxAttempts: 4, baseDelay: TimeSpan.FromSeconds(1));
        var calls = 0;
        var sw = Stopwatch.StartNew();

        var result = await retry.ExecuteAsync(() =>
            ++calls < 4 ? throw new TimeoutException("flaky dependency")
                        : Task.FromResult("ok"));

        sw.Stop();
        Assert.Equal("ok", result);
        Assert.Equal(4, retry.Attempts);
        // 1s + 2s + 4s of genuine wall-clock waiting:
        Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(7), $"only waited {sw.Elapsed}");
    }
}
