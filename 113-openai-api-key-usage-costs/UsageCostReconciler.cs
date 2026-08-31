using System.Text.Json;

namespace OpenAiApiKeyUsageCosts;

internal static class FixtureLoader
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false
    };

    internal static IReadOnlyList<UsagePage> LoadUsagePages(string fixtureDirectory) =>
        LoadPageChain<UsagePage>(
            fixtureDirectory,
            "usage-page-1.json",
            cursor => cursor switch
            {
                "usage-cursor-2" => "usage-page-2.json",
                _ => throw new InvalidDataException($"Unknown usage cursor: {cursor}")
            },
            page => page.HasMore,
            page => page.NextPage);

    internal static IReadOnlyList<CostPage> LoadCostPages(string fixtureDirectory) =>
        LoadPageChain<CostPage>(
            fixtureDirectory,
            "costs-page-1.json",
            cursor => cursor switch
            {
                "cost-cursor-2" => "costs-page-2.json",
                _ => throw new InvalidDataException($"Unknown cost cursor: {cursor}")
            },
            page => page.HasMore,
            page => page.NextPage);

    private static IReadOnlyList<TPage> LoadPageChain<TPage>(
        string fixtureDirectory,
        string firstFile,
        Func<string, string> resolveCursor,
        Func<TPage, bool> hasMore,
        Func<TPage, string?> nextPage)
    {
        var pages = new List<TPage>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? file = firstFile;

        while (file is not null)
        {
            var path = Path.Combine(fixtureDirectory, file);
            var page = JsonSerializer.Deserialize<TPage>(File.ReadAllText(path), JsonOptions)
                ?? throw new InvalidDataException($"Could not parse fixture: {file}");
            pages.Add(page);

            if (!hasMore(page))
            {
                file = null;
                continue;
            }

            var cursor = nextPage(page);
            if (string.IsNullOrWhiteSpace(cursor))
            {
                throw new InvalidDataException("A page with has_more=true must provide next_page.");
            }

            if (!seenCursors.Add(cursor))
            {
                throw new InvalidDataException($"Pagination cursor repeated: {cursor}");
            }

            file = resolveCursor(cursor);
        }

        return pages;
    }
}

internal static class PaginationValidator
{
    internal static void ValidateUsage(IReadOnlyList<UsagePage> pages) =>
        Validate(pages, page => page.HasMore, page => page.NextPage);

    internal static void ValidateCosts(IReadOnlyList<CostPage> pages) =>
        Validate(pages, page => page.HasMore, page => page.NextPage);

    private static void Validate<TPage>(
        IReadOnlyList<TPage> pages,
        Func<TPage, bool> hasMore,
        Func<TPage, string?> nextPage)
    {
        if (pages.Count == 0)
        {
            throw new InvalidDataException("At least one page is required.");
        }

        var cursors = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < pages.Count; index++)
        {
            var page = pages[index];
            var more = hasMore(page);
            var cursor = nextPage(page);
            var isLast = index == pages.Count - 1;

            if (more && string.IsNullOrWhiteSpace(cursor))
            {
                throw new InvalidDataException("A page with has_more=true must provide next_page.");
            }

            if (!more && !string.IsNullOrWhiteSpace(cursor))
            {
                throw new InvalidDataException("A terminal page must not provide next_page.");
            }

            if (!isLast && !more)
            {
                throw new InvalidDataException("A non-final page ended the chain early.");
            }

            if (isLast && more)
            {
                throw new InvalidDataException("The loaded page chain is incomplete.");
            }

            if (!string.IsNullOrWhiteSpace(cursor) && !cursors.Add(cursor))
            {
                throw new InvalidDataException($"Pagination cursor repeated: {cursor}");
            }
        }
    }
}

internal static class UsageCostReconciler
{
    internal const string UnattributedKey = "<unattributed>";
    private const long SecondsPerDay = 86_400;

    internal static IReadOnlyList<ReconciliationRow> Reconcile(
        IReadOnlyList<UsagePage> usagePages,
        IReadOnlyList<CostPage> costPages)
    {
        PaginationValidator.ValidateUsage(usagePages);
        PaginationValidator.ValidateCosts(costPages);

        var usage = BuildUsageIndex(usagePages);
        var costs = BuildCostIndex(costPages);
        var keys = usage.Keys.Concat(costs.Keys).Distinct().OrderBy(key => key.StartTime)
            .ThenBy(key => key.ApiKeyId, StringComparer.Ordinal);
        var rows = new List<ReconciliationRow>();

        foreach (var key in keys)
        {
            var hasUsage = usage.TryGetValue(key, out var usageValue);
            var hasCost = costs.TryGetValue(key, out var costValue);
            var status = (hasUsage, hasCost) switch
            {
                (true, true) => ReconciliationStatus.Matched,
                (true, false) => ReconciliationStatus.UsageOnly,
                (false, true) => ReconciliationStatus.CostOnly,
                _ => throw new InvalidOperationException("The full-outer key union produced an empty row.")
            };

            rows.Add(new ReconciliationRow(
                key,
                hasUsage,
                usageValue?.InputTokens ?? 0,
                usageValue?.OutputTokens ?? 0,
                usageValue?.ModelRequests ?? 0,
                hasCost,
                costValue?.Amount ?? 0m,
                costValue?.Currency,
                status));
        }

        return rows;
    }

    private static Dictionary<DailyApiKey, UsageValue> BuildUsageIndex(IEnumerable<UsagePage> pages)
    {
        var index = new Dictionary<DailyApiKey, UsageValue>();
        foreach (var bucket in pages.SelectMany(page => page.Data))
        {
            ValidateDailyBucket(bucket.StartTime, bucket.EndTime);
            foreach (var result in bucket.Results)
            {
                var key = new DailyApiKey(bucket.StartTime, bucket.EndTime, Normalize(result.ApiKeyId));
                var value = new UsageValue(result.InputTokens, result.OutputTokens, result.ModelRequests);
                if (!index.TryAdd(key, value))
                {
                    throw new InvalidDataException($"Duplicate usage bucket/api_key_id: {key}");
                }
            }
        }

        return index;
    }

    private static Dictionary<DailyApiKey, CostValue> BuildCostIndex(IEnumerable<CostPage> pages)
    {
        var index = new Dictionary<DailyApiKey, CostValue>();
        var currencies = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var bucket in pages.SelectMany(page => page.Data))
        {
            ValidateDailyBucket(bucket.StartTime, bucket.EndTime);
            foreach (var result in bucket.Results)
            {
                if (string.IsNullOrWhiteSpace(result.Amount.Currency))
                {
                    throw new InvalidDataException("Every cost amount must have a currency.");
                }

                currencies.Add(result.Amount.Currency);
                if (currencies.Count > 1)
                {
                    throw new InvalidDataException("Mixed currencies cannot be combined in one report.");
                }

                var key = new DailyApiKey(bucket.StartTime, bucket.EndTime, Normalize(result.ApiKeyId));
                var value = new CostValue(result.Amount.Value, result.Amount.Currency.ToLowerInvariant());
                if (!index.TryAdd(key, value))
                {
                    throw new InvalidDataException($"Duplicate cost bucket/api_key_id: {key}");
                }
            }
        }

        return index;
    }

    private static string Normalize(string? apiKeyId) =>
        string.IsNullOrWhiteSpace(apiKeyId) ? UnattributedKey : apiKeyId;

    private static void ValidateDailyBucket(long startTime, long endTime)
    {
        if (endTime - startTime != SecondsPerDay)
        {
            throw new InvalidDataException("Usage and cost buckets must both use the 1d interval.");
        }
    }

    private sealed record UsageValue(long InputTokens, long OutputTokens, long ModelRequests);
    private sealed record CostValue(decimal Amount, string Currency);
}
