using Sample080;

var passed = 0;

var brokenServer = new FakeToolPager();
var brokenTools = await McpPaginationClient.ListAllBrokenAsync(brokenServer);
Expect(brokenTools.SequenceEqual(["catalog.search"]), "broken loop stops after page one");
Expect(brokenServer.RequestedCursors.Count == 1, "broken loop makes one request");

var fixedServer = new FakeToolPager();
var fixedTools = await McpPaginationClient.ListAllAsync(fixedServer);
Expect(
    fixedTools.SequenceEqual(["catalog.search", "catalog.lookup", "catalog.health"]),
    "fixed loop returns every tool");
Expect(fixedServer.RequestedCursors.Count == 3, "fixed loop makes three requests");
Expect(fixedServer.RequestedCursors[0] is null, "first request omits the cursor");
Expect(fixedServer.RequestedCursors[1] == string.Empty, "empty cursor is forwarded");
Expect(
    fixedServer.RequestedCursors[2] == FakeToolPager.OpaqueCursor,
    "opaque cursor is forwarded without modification");

var invalidCursorCode = 0;
var invalidCursorServer = new FakeToolPager();
try
{
    await invalidCursorServer.ListToolsAsync("decoded-page-2");
}
catch (McpProtocolException exception)
{
    invalidCursorCode = exception.Code;
}

Expect(invalidCursorCode == -32602, "invalid cursor returns Invalid params");

Console.WriteLine($"Broken tools: {string.Join(", ", brokenTools)}");
Console.WriteLine($"Fixed tools: {string.Join(", ", fixedTools)}");
Console.WriteLine(
    $"Forwarded cursors: {string.Join(" | ", fixedServer.RequestedCursors.Select(Describe))}");
Console.WriteLine($"Invalid cursor code: {invalidCursorCode}");
Console.WriteLine($"Checks passed: {passed}/8");

static string Describe(string? cursor) => cursor switch
{
    null => "<missing>",
    "" => "<empty>",
    _ => cursor
};

void Expect(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"Check failed: {name}");
    }

    passed++;
}
