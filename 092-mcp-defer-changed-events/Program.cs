using ModelContextProtocol.Server;

var verifier = new Verifier();

verifier.Run(
    "unbatched additions raised three changes",
    () =>
    {
        var (tools, changes) = ObserveCollection();

        AddTools(tools, "search", "summarize", "export");

        return tools.Count == 3 && changes() == 3;
    });

verifier.Run(
    "one scope coalesced three additions",
    () =>
    {
        var (tools, changes) = ObserveCollection();
        var stayedQuietInsideScope = false;

        using (tools.DeferChangedEvents())
        {
            AddTools(tools, "search", "summarize", "export");
            stayedQuietInsideScope = changes() == 0;
        }

        return stayedQuietInsideScope && tools.Count == 3 && changes() == 1;
    });

verifier.Run(
    "nested scopes waited for the outer dispose",
    () =>
    {
        var (tools, changes) = ObserveCollection();
        var outer = tools.DeferChangedEvents();

        using (tools.DeferChangedEvents())
        {
            tools.Add(CreateTool("search"));
        }

        var innerDisposeStayedQuiet = changes() == 0;
        tools.Add(CreateTool("summarize"));
        outer.Dispose();

        return innerDisposeStayedQuiet && tools.Count == 2 && changes() == 1;
    });

verifier.Run(
    "an empty scope raised no change",
    () =>
    {
        var (tools, changes) = ObserveCollection();

        using (tools.DeferChangedEvents())
        {
        }

        return tools.Count == 0 && changes() == 0;
    });

verifier.Run(
    "Clear on an empty collection still raised one signal",
    () =>
    {
        var (tools, changes) = ObserveCollection();
        var stayedQuietInsideScope = false;

        using (tools.DeferChangedEvents())
        {
            tools.Clear();
            stayedQuietInsideScope = changes() == 0;
        }

        return stayedQuietInsideScope && tools.Count == 0 && changes() == 1;
    });

verifier.Run(
    "a duplicate TryAdd raised no extra change",
    () =>
    {
        var (tools, changes) = ObserveCollection();
        tools.Add(CreateTool("search"));
        var beforeDuplicate = changes();

        var added = tools.TryAdd(CreateTool("search"));

        return !added && tools.Count == 1 && changes() == beforeDuplicate;
    });

verifier.Run(
    "exception disposal flushed one real change",
    () =>
    {
        var (tools, changes) = ObserveCollection();

        try
        {
            using (tools.DeferChangedEvents())
            {
                tools.Add(CreateTool("search"));
                throw new ExpectedBatchException();
            }
        }
        catch (ExpectedBatchException)
        {
        }

        return tools.Count == 1 && changes() == 1;
    });

verifier.Run(
    "concurrent additions produced one deterministic change",
    () =>
    {
        var (tools, changes) = ObserveCollection();

        using (tools.DeferChangedEvents())
        {
            Parallel.For(
                0,
                8,
                index => tools.Add(CreateTool($"tool-{index:D2}")));
        }

        var expectedNames = Enumerable.Range(0, 8)
            .Select(index => $"tool-{index:D2}")
            .Order(StringComparer.Ordinal)
            .ToArray();
        var actualNames = tools.PrimitiveNames
            .Order(StringComparer.Ordinal)
            .ToArray();

        return changes() == 1 && actualNames.SequenceEqual(expectedNames);
    });

verifier.Complete();

static (McpServerPrimitiveCollection<McpServerTool> Tools, Func<int> Changes)
    ObserveCollection()
{
    var tools = new McpServerPrimitiveCollection<McpServerTool>();
    var changeCount = 0;
    tools.Changed += (_, _) => Interlocked.Increment(ref changeCount);
    return (tools, () => Volatile.Read(ref changeCount));
}

static void AddTools(
    McpServerPrimitiveCollection<McpServerTool> tools,
    params string[] names)
{
    foreach (var name in names)
    {
        tools.Add(CreateTool(name));
    }
}

static McpServerTool CreateTool(string name) =>
    McpServerTool.Create(
        (string input) => input,
        new McpServerToolCreateOptions
        {
            Name = name,
            Description = $"Offline {name} fixture.",
            ReadOnly = true,
            OpenWorld = false,
        });

internal sealed class ExpectedBatchException : Exception;

internal sealed class Verifier
{
    private int _passed;
    private int _total;

    public void Run(string name, Func<bool> check)
    {
        _total++;
        if (!check())
        {
            Console.Error.WriteLine($"FAIL: {name}");
            Environment.ExitCode = 1;
            return;
        }

        _passed++;
        Console.WriteLine($"PASS: {name}");
    }

    public void Complete()
    {
        Console.WriteLine($"Verifier passed {_passed}/{_total}.");
        if (_passed != _total)
        {
            Environment.ExitCode = 1;
        }
    }
}
