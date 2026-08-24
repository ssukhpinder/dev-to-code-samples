namespace Sample080;

internal interface IToolPager
{
    Task<ToolPage> ListToolsAsync(
        string? cursor,
        CancellationToken cancellationToken = default);
}

internal sealed record ToolPage(
    IReadOnlyList<string> Tools,
    string? NextCursor);

internal static class McpPaginationClient
{
    public static async Task<IReadOnlyList<string>> ListAllBrokenAsync(
        IToolPager pager,
        CancellationToken cancellationToken = default)
    {
        var tools = new List<string>();
        string? cursor = null;

        do
        {
            var page = await pager.ListToolsAsync(cursor, cancellationToken);
            tools.AddRange(page.Tools);
            cursor = page.NextCursor;
        }
        while (!string.IsNullOrEmpty(cursor));

        return tools;
    }

    public static async Task<IReadOnlyList<string>> ListAllAsync(
        IToolPager pager,
        int maxPages = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxPages);

        var tools = new List<string>();
        string? cursor = null;

        for (var pageNumber = 1; pageNumber <= maxPages; pageNumber++)
        {
            var page = await pager.ListToolsAsync(cursor, cancellationToken);
            tools.AddRange(page.Tools);

            if (page.NextCursor is null)
            {
                return tools;
            }

            cursor = page.NextCursor;
        }

        throw new InvalidOperationException(
            $"Pagination exceeded the configured limit of {maxPages} pages.");
    }
}
