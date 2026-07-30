using Microsoft.Extensions.Time.Testing;

namespace TimeProviderTests;

/// <summary>
/// The "after" picture: same scenario, but the test owns the clock.
/// Seven virtual seconds pass; approximately zero real ones do.
///
/// The SetSynchronizationContext(null) line is load-bearing. xunit v2 runs tests
/// under an AsyncTestSyncContext, and a pending await — even with
/// ConfigureAwait(false) — won't inline its continuation on a thread that has a
/// current SynchronizationContext. So Advance() completes the delay, the
/// continuation gets queued to the thread pool instead of running inline, and the
/// next Task.Delay registers AFTER all your Advance calls have already happened.
/// Its due time lands in a future the clock never reaches: deadlock.
/// With the context cleared, each Advance() runs the continuation synchronously
/// and the test is fully deterministic.
/// </summary>
public class RetryFakeClockTests
{
    [Fact]
    public async Task Same_scenario_fake_clock()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var fake = new FakeTimeProvider();
        var retry = new TransientRetry(fake, maxAttempts: 4, baseDelay: TimeSpan.FromSeconds(1));
        var calls = 0;

        var task = retry.ExecuteAsync(() =>
            ++calls < 4 ? throw new TimeoutException("flaky dependency")
                        : Task.FromResult("ok"));

        Assert.False(task.IsCompleted);          // attempt 1 failed, parked on the 1s backoff
        fake.Advance(TimeSpan.FromSeconds(1));   // attempt 2 fails, parks on 2s
        fake.Advance(TimeSpan.FromSeconds(2));   // attempt 3 fails, parks on 4s
        Assert.False(task.IsCompleted);          // mid-backoff assertion: try writing THIS with Thread.Sleep
        fake.Advance(TimeSpan.FromSeconds(4));   // attempt 4 succeeds

        Assert.Equal("ok", await task);
        Assert.Equal(4, retry.Attempts);
    }

    [Fact]
    public async Task Gives_up_after_max_attempts_fake_clock()
    {
        SynchronizationContext.SetSynchronizationContext(null);

        var fake = new FakeTimeProvider();
        var retry = new TransientRetry(fake, maxAttempts: 4, baseDelay: TimeSpan.FromSeconds(1));

        var task = retry.ExecuteAsync<string>(() => throw new TimeoutException("still down"));

        fake.Advance(TimeSpan.FromSeconds(1));
        fake.Advance(TimeSpan.FromSeconds(2));
        fake.Advance(TimeSpan.FromSeconds(4));   // attempt 4 fails too — no retries left

        await Assert.ThrowsAsync<TimeoutException>(() => task);
        Assert.Equal(4, retry.Attempts);
    }
}
