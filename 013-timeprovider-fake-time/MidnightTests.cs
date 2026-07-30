using Microsoft.Extensions.Time.Testing;

namespace TimeProviderTests;

/// <summary>
/// The bug class you can't reproduce on demand with a real clock: behavior at a
/// midnight boundary, in a timezone your CI server isn't in.
/// </summary>
public class MidnightTests
{
    [Fact]
    public void Quota_resets_at_local_midnight_even_though_utc_date_never_changed()
    {
        // 18:20 UTC on July 30 == 23:50 on July 30 in Kolkata (UTC+05:30)
        var fake = new FakeTimeProvider(new DateTimeOffset(2026, 7, 30, 18, 20, 0, TimeSpan.Zero));
        fake.SetLocalTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata"));

        var quota = new DailyQuota(fake, limit: 2);

        Assert.True(quota.TryConsume());
        Assert.True(quota.TryConsume());
        Assert.False(quota.TryConsume());        // exhausted at 23:50 local

        fake.Advance(TimeSpan.FromMinutes(20));  // now 00:10 local on July 31

        // The UTC calendar hasn't turned over — key the day on GetUtcNow().Date
        // and this reset silently doesn't happen until 05:30 in the morning:
        Assert.Equal(new DateOnly(2026, 7, 30), DateOnly.FromDateTime(fake.GetUtcNow().UtcDateTime));

        Assert.True(quota.TryConsume());         // fresh local day, quota is back
    }
}
