using System.Globalization;

string[] fileNames = ["file10.txt", "file2.txt", "file02.txt", "file9.txt"];

StringComparer numericComparer = StringComparer.Create(
    CultureInfo.InvariantCulture,
    CompareOptions.NumericOrdering);

if (args.Contains("--verify", StringComparer.Ordinal))
{
    return Verify(fileNames, numericComparer);
}

Console.WriteLine($"Ordinal: {string.Join(" | ", fileNames.Order(StringComparer.Ordinal))}");
Console.WriteLine($"Numeric: {string.Join(" | ", fileNames.Order(numericComparer))}");
Console.WriteLine($"file2.txt equals file02.txt: {numericComparer.Equals("file2.txt", "file02.txt")}");
Console.WriteLine($"v1.5 equals v1.05: {numericComparer.Equals("v1.5", "v1.05")}");
Console.WriteLine();
Console.WriteLine("Run with --verify for deterministic checks.");
return 0;

static int Verify(string[] fileNames, StringComparer numericComparer)
{
    var passed = 0;

    Check(
        fileNames.Order(StringComparer.Ordinal).SequenceEqual(
            ["file02.txt", "file10.txt", "file2.txt", "file9.txt"]),
        "ordinal sorting exposes the file10-before-file9 problem");

    Check(
        fileNames.Order(numericComparer).SequenceEqual(
            ["file2.txt", "file02.txt", "file9.txt", "file10.txt"]),
        "numeric sorting places file9 before file10");

    Check(
        numericComparer.Equals("file2.txt", "file02.txt"),
        "leading zeroes do not change numeric equality");

    var distinctNames = new HashSet<string>(numericComparer)
    {
        "file2.txt",
        "file02.txt"
    };
    Check(
        distinctNames.Count == 1,
        "hash-based collections use the same equality rule");

    Check(
        numericComparer.Equals("v1.5", "v1.05"),
        "punctuation splits digit runs, so this is not decimal comparison");

    var indexOperationRejected = false;
    try
    {
        _ = CultureInfo.InvariantCulture.CompareInfo.IndexOf(
            "file10.txt",
            "10",
            CompareOptions.NumericOrdering);
    }
    catch (ArgumentException)
    {
        indexOperationRejected = true;
    }

    Check(
        indexOperationRejected,
        "NumericOrdering is rejected by index-based string operations");

    Console.WriteLine($"PASS {passed}/6");
    return 0;

    void Check(bool condition, string description)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {description}");
        }

        passed++;
        Console.WriteLine($"PASS: {description}");
    }
}
