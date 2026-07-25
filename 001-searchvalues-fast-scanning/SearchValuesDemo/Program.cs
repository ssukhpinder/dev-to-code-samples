using System.Buffers;
using System.Diagnostics;
using System.Text;

// Two small experiments comparing ways to scan text for a set of characters.
// Run with: dotnet run -c Release

const int WarmupRounds = 2;
const int MeasuredRounds = 5;

char[] delimiterArray = ['=', '&', ';', '?', '#'];
SearchValues<char> delimiters = SearchValues.Create(['=', '&', ';', '?', '#']);

// ---------- Experiment 1: one big buffer ----------
Console.WriteLine("Building a ~32 MB fake log buffer...");
string bigText = BuildFakeLog(sizeInMb: 32);
Console.WriteLine($"Buffer length: {bigText.Length:N0} chars\n");

Measure("IndexOfAny(char[])      ", () => CountWithCharArray(bigText, delimiterArray));
Measure("manual char loop        ", () => CountWithManualLoop(bigText));
Measure("SearchValues<char>      ", () => CountWithSearchValues(bigText, delimiters));


// ---------- Experiment 1b: same buffer, but matches are RARE ----------
Console.WriteLine("\nSame size buffer, but delimiters are rare (~1 per 40 KB):");
string sparseText = BuildSparseLog(sizeInMb: 32);

char[] bigSet = ['=', '&', ';', '?', '#', '%', '+', '@', '!', '~', '^', '|'];
SearchValues<char> bigSetSv = SearchValues.Create(bigSet);

Measure("IndexOfAny(char[]) x5   ", () => CountWithCharArray(sparseText, delimiterArray));
Measure("SearchValues x5         ", () => CountWithSearchValues(sparseText, delimiters));
Measure("IndexOfAny(char[]) x12  ", () => CountWithCharArray(sparseText, bigSet));
Measure("SearchValues x12        ", () => CountWithSearchValuesSet(sparseText, bigSetSv));

// ---------- Experiment 2: a million short lines ----------
Console.WriteLine("\nNow 1,000,000 short lines (~100 chars each):");
string[] lines = BuildShortLines(1_000_000);

Measure("static SearchValues     ", () => CountLinesStatic(lines, delimiters));
Measure("SearchValues per call   ", () => CountLinesCreatePerCall(lines));

return;

// ---------- helpers ----------

static long CountWithCharArray(string text, char[] set)
{
    long count = 0;
    int i = 0;
    ReadOnlySpan<char> span = text;
    while ((i = text.IndexOfAny(set, i)) >= 0) { count++; i++; if (i >= span.Length) break; }
    return count;
}

static long CountWithManualLoop(string text)
{
    long count = 0;
    foreach (char c in text)
        if (c is '=' or '&' or ';' or '?' or '#') count++;
    return count;
}

static long CountWithSearchValues(string text, SearchValues<char> set)
{
    long count = 0;
    ReadOnlySpan<char> span = text;
    int i;
    while ((i = span.IndexOfAny(set)) >= 0) { count++; span = span[(i + 1)..]; }
    return count;
}

static long CountLinesStatic(string[] lines, SearchValues<char> set)
{
    long count = 0;
    foreach (string line in lines)
        if (line.AsSpan().ContainsAny(set)) count++;
    return count;
}

static long CountLinesCreatePerCall(string[] lines)
{
    long count = 0;
    foreach (string line in lines)
    {
        var set = SearchValues.Create(['=', '&', ';', '?', '#']); // the trap
        if (line.AsSpan().ContainsAny(set)) count++;
    }
    return count;
}

void Measure(string label, Func<long> action)
{
    for (int w = 0; w < WarmupRounds; w++) action();
    var times = new List<double>(MeasuredRounds);
    long result = 0;
    for (int r = 0; r < MeasuredRounds; r++)
    {
        var sw = Stopwatch.StartNew();
        result = action();
        sw.Stop();
        times.Add(sw.Elapsed.TotalMilliseconds);
    }
    Console.WriteLine($"{label}: {times.Min(),8:F1} ms (best of {MeasuredRounds})   matches: {result:N0}");
}

static string BuildFakeLog(int sizeInMb)
{
    var rng = new Random(42);
    var sb = new StringBuilder(sizeInMb * 1024 * 1024 / 2);
    string[] fragments =
    [
        "GET /api/orders/", " HTTP/1.1 200 ", "user_id", "session", "trace",
        "some plain message text with nothing special in it ",
        "cache miss for key ", "elapsed_ms", "region-east-", "payload size "
    ];
    while (sb.Length < sizeInMb * 1024 * 1024 / 2)
    {
        sb.Append(fragments[rng.Next(fragments.Length)]);
        sb.Append(rng.Next(100_000));
        if (rng.Next(6) == 0) sb.Append('=').Append(rng.Next(1000));
        if (rng.Next(9) == 0) sb.Append('&');
        if (rng.Next(14) == 0) sb.Append(';');
        sb.Append(' ');
    }
    return sb.ToString();
}


static long CountWithSearchValuesSet(string text, SearchValues<char> set)
{
    long count = 0;
    ReadOnlySpan<char> span = text;
    int i;
    while ((i = span.IndexOfAny(set)) >= 0) { count++; span = span[(i + 1)..]; }
    return count;
}

static string BuildSparseLog(int sizeInMb)
{
    var rng = new Random(42);
    var sb = new StringBuilder(sizeInMb * 1024 * 1024 / 2);
    string filler = "the quick brown fox jumps over the lazy dog while the service keeps logging plain text ";
    while (sb.Length < sizeInMb * 1024 * 1024 / 2)
    {
        for (int i = 0; i < 250; i++) { sb.Append(filler); sb.Append(rng.Next(100000)); sb.Append(' '); }
        sb.Append('=');
    }
    return sb.ToString();
}

static string[] BuildShortLines(int count)
{
    var rng = new Random(7);
    var lines = new string[count];
    for (int i = 0; i < count; i++)
        lines[i] = $"GET /api/items/{rng.Next(9999)}?page={rng.Next(50)}&size=20 took {rng.Next(200)}ms region=east-{rng.Next(4)}";
    return lines;
}
