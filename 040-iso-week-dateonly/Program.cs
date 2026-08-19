using System.Globalization;

var entries = new LedgerEntry[]
{
    new(new DateOnly(2025, 1, 1), 5m),
    new(new DateOnly(2025, 12, 29), 20m),
    new(new DateOnly(2026, 1, 4), 30m),
    new(new DateOnly(2026, 1, 5), 40m),
    new(new DateOnly(2027, 1, 1), 50m),
};

Console.WriteLine("Correct ISO groups:");

foreach (var group in entries
             .GroupBy(entry => IsoWeekKey.From(entry.Date))
             .OrderBy(group => group.Key.IsoYear)
             .ThenBy(group => group.Key.Week))
{
    var dates = string.Join(
        ", ",
        group.Select(entry => entry.Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)));

    Console.WriteLine($"{group.Key}: {dates} total={group.Sum(entry => entry.Amount):0.00}");
}

var failures = 0;

Check(
    IsoWeekKey.From(new DateOnly(2025, 12, 29)) == new IsoWeekKey(2026, 1),
    "2025-12-29 belongs to ISO year 2026, week 01");

Check(
    IsoWeekKey.From(new DateOnly(2027, 1, 1)) == new IsoWeekKey(2026, 53),
    "2027-01-01 belongs to ISO year 2026, week 53");

var misleadingGregorianGroup = entries
    .Where(entry =>
        entry.Date.Year == 2025 &&
        ISOWeek.GetWeekOfYear(entry.Date) == 1)
    .Select(entry => entry.Date)
    .ToArray();

Check(
    misleadingGregorianGroup.SequenceEqual(
        [new DateOnly(2025, 1, 1), new DateOnly(2025, 12, 29)]),
    "DateOnly.Year plus ISO week incorrectly joins dates 362 days apart");

var correctWeek = entries
    .Where(entry => IsoWeekKey.From(entry.Date) == new IsoWeekKey(2026, 1))
    .ToArray();

Check(
    correctWeek.Select(entry => entry.Date).SequenceEqual(
        [new DateOnly(2025, 12, 29), new DateOnly(2026, 1, 4)]) &&
    correctWeek.Sum(entry => entry.Amount) == 50m,
    "ISO year and week group the complete 2026-W01 range");

var weekOne = new IsoWeekKey(2026, 1);

Check(
    weekOne.Monday == new DateOnly(2025, 12, 29) &&
    weekOne.Sunday == new DateOnly(2026, 1, 4),
    "ISOWeek.ToDateOnly reconstructs the Monday-to-Sunday range");

Check(
    entries.All(entry =>
    {
        var key = IsoWeekKey.From(entry.Date);
        return ISOWeek.ToDateOnly(key.IsoYear, key.Week, entry.Date.DayOfWeek) == entry.Date;
    }),
    "every fixture date round-trips through its ISO week parts");

Console.WriteLine($"Verifier: {6 - failures}/6 passed");
return failures == 0 ? 0 : 1;

void Check(bool condition, string message)
{
    if (condition)
    {
        Console.WriteLine($"PASS {message}");
        return;
    }

    failures++;
    Console.WriteLine($"FAIL {message}");
}

internal readonly record struct LedgerEntry(DateOnly Date, decimal Amount);

internal readonly record struct IsoWeekKey(int IsoYear, int Week)
{
    public DateOnly Monday => ISOWeek.ToDateOnly(IsoYear, Week, DayOfWeek.Monday);

    public DateOnly Sunday => Monday.AddDays(6);

    public static IsoWeekKey From(DateOnly date) =>
        new(ISOWeek.GetYear(date), ISOWeek.GetWeekOfYear(date));

    public override string ToString() => $"{IsoYear}-W{Week:00}";
}
