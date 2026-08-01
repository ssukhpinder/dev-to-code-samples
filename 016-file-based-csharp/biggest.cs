#!/usr/bin/env dotnet
#:package Humanizer@3.0.10

// biggest.cs — list the largest files under a directory.
// Run it straight from the file, no project needed (.NET 10):
//   dotnet run biggest.cs -- /path/to/scan 10
// Or make it executable and run it like any shell script:
//   chmod +x biggest.cs && ./biggest.cs /path/to/scan

using Humanizer;

var root = args.Length > 0 ? args[0] : ".";
var top = args.Length > 1 && int.TryParse(args[1], out var n) ? n : 10;

if (!Directory.Exists(root))
{
    Console.Error.WriteLine($"No such directory: {root}");
    return 1;
}

var files = new DirectoryInfo(root)
    .EnumerateFiles("*", new EnumerationOptions
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint
    })
    .OrderByDescending(f => f.Length)
    .Take(top)
    .ToList();

if (files.Count == 0)
{
    Console.WriteLine($"Nothing found under {root}.");
    return 0;
}

Console.WriteLine($"Top {files.Count} files under {Path.GetFullPath(root)}:\n");

foreach (var f in files)
{
    var size = f.Length.Bytes().Humanize("#.#");
    var age = (DateTime.UtcNow - f.LastWriteTimeUtc).Humanize();
    Console.WriteLine($"{size,10}  {f.FullName}  (modified {age} ago)");
}

Console.WriteLine($"\nTotal: {files.Sum(f => f.Length).Bytes().Humanize("#.#")}");
return 0;
