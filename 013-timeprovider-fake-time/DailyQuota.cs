namespace TimeProviderTests;

/// <summary>
/// "N requests per day, resets at midnight" — where "midnight" means the user's
/// local midnight, so the day key comes from GetLocalNow(), not GetUtcNow().
/// </summary>
public sealed class DailyQuota(TimeProvider clock, int limit)
{
    private DateOnly _day;
    private int _used;

    public bool TryConsume()
    {
        var today = DateOnly.FromDateTime(clock.GetLocalNow().DateTime);
        if (today != _day)
        {
            _day = today;
            _used = 0;
        }

        if (_used >= limit) return false;
        _used++;
        return true;
    }
}
