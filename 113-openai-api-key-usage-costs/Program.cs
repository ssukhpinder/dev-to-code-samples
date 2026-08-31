using System.Globalization;
using OpenAiApiKeyUsageCosts;

var fixtureDirectory = Path.Combine(AppContext.BaseDirectory, "fixtures");
var usagePages = FixtureLoader.LoadUsagePages(fixtureDirectory);
var costPages = FixtureLoader.LoadCostPages(fixtureDirectory);
var report = UsageCostReconciler.Reconcile(usagePages, costPages);

Console.WriteLine("OpenAI daily usage-cost reconciliation by api_key_id");
Console.WriteLine("date       api_key_id       input output requests cost     status");
foreach (var row in report)
{
    var date = DateTimeOffset.FromUnixTimeSeconds(row.Key.StartTime)
        .UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    var cost = row.HasCost
        ? FormattableString.Invariant($"{row.Amount:0.0000} {row.Currency}")
        : "-";
    Console.WriteLine(FormattableString.Invariant(
        $"{date} {row.Key.ApiKeyId,-16} {row.InputTokens,5} {row.OutputTokens,6} {row.ModelRequests,8} {cost,-12} {row.Status}"));
}

var checks = new VerificationChecks();
checks.Check(
    "followed both paginated fixture chains",
    usagePages.Count == 2 && costPages.Count == 2);
checks.Check(
    "built the six-row full-outer key set",
    report.Count == 6);
checks.Check(
    "classified four matched, one usage-only, and one cost-only row",
    report.Count(row => row.Status == ReconciliationStatus.Matched) == 4 &&
    report.Count(row => row.Status == ReconciliationStatus.UsageOnly) == 1 &&
    report.Count(row => row.Status == ReconciliationStatus.CostOnly) == 1);
checks.Check(
    "preserved the null api_key_id bucket as unattributed",
    report.Any(row => row.Key.ApiKeyId == UsageCostReconciler.UnattributedKey &&
        row.HasUsage && row.HasCost));
checks.Check(
    "kept token counts and billed amount as separate measures",
    report.Any(row => row.Key.StartTime == 1_785_542_400 &&
        row.Key.ApiKeyId == "key_alpha" && row.InputTokens == 1_200 &&
        row.OutputTokens == 300 && row.ModelRequests == 3 &&
        row.Amount == 0.1200m && row.Currency == "usd"));
checks.ExpectInvalidData(
    "rejected has_more without next_page",
    () => PaginationValidator.ValidateUsage(
        [new UsagePage([], true, null)]));
checks.ExpectInvalidData(
    "rejected duplicate day and api_key_id dimensions",
    () => UsageCostReconciler.Reconcile(
        [new UsagePage(
            [new UsageBucket(0, 86_400,
                [new UsageResult(1, 1, 1, "duplicate"), new UsageResult(2, 2, 2, "duplicate")])],
            false,
            null)],
        [new CostPage([], false, null)]));
checks.ExpectInvalidData(
    "rejected mixed currencies",
    () => UsageCostReconciler.Reconcile(
        [new UsagePage([], false, null)],
        [new CostPage(
            [new CostBucket(0, 86_400,
                [new CostResult(new Money(1m, "usd"), "key_a"),
                 new CostResult(new Money(1m, "eur"), "key_b")])],
            false,
            null)]));
checks.ExpectInvalidData(
    "rejected a non-daily usage bucket",
    () => UsageCostReconciler.Reconcile(
        [new UsagePage(
            [new UsageBucket(0, 3_600, [new UsageResult(1, 1, 1, "key_a")])],
            false,
            null)],
        [new CostPage([], false, null)]));

checks.Complete();

internal sealed class VerificationChecks
{
    private int _passed;
    private int _total;

    internal void Check(string name, bool condition)
    {
        _total++;
        if (!condition)
        {
            throw new InvalidOperationException($"[FAIL] {name}");
        }

        _passed++;
        Console.WriteLine($"[PASS] {name}");
    }

    internal void ExpectInvalidData(string name, Action action)
    {
        try
        {
            action();
        }
        catch (InvalidDataException)
        {
            Check(name, true);
            return;
        }

        Check(name, false);
    }

    internal void Complete()
    {
        Console.WriteLine($"PASS: {_passed}/{_total} API-key usage-cost checks passed");
    }
}
