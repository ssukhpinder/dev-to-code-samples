namespace Sample080;

internal sealed class FakeToolPager : IToolPager
{
    internal const string OpaqueCursor = "opaque:/+==";

    private readonly List<string?> _requestedCursors = [];

    public IReadOnlyList<string?> RequestedCursors => _requestedCursors;

    public Task<ToolPage> ListToolsAsync(
        string? cursor,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _requestedCursors.Add(cursor);

        var page = cursor switch
        {
            null => new ToolPage(["catalog.search"], string.Empty),
            "" => new ToolPage(["catalog.lookup"], OpaqueCursor),
            OpaqueCursor => new ToolPage(["catalog.health"], null),
            _ => throw new McpProtocolException(
                -32602,
                $"Invalid cursor: {cursor}")
        };

        return Task.FromResult(page);
    }
}

internal sealed class McpProtocolException(int code, string message)
    : Exception(message)
{
    public int Code { get; } = code;
}
