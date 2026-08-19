using System.IO.Compression;
using System.Text;
using AsyncZipCleanup;

string testRoot = Path.Combine(
    Path.GetTempPath(),
    $"async-zip-cleanup-{Guid.NewGuid():N}");
Directory.CreateDirectory(testRoot);

try
{
    await VerifySuccessfulExtractionAsync(testRoot);
    await VerifyCancellationCleanupAsync(testRoot);
    await VerifyCorruptArchiveCleanupAsync(testRoot);
    await VerifyExistingDestinationIsPreservedAsync(testRoot);
    await VerifyDestinationRaceIsPreservedAsync(testRoot);

    Console.WriteLine("All 5 checks passed.");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine($"FAIL: {exception.Message}");
    return 1;
}
finally
{
    if (Directory.Exists(testRoot))
    {
        Directory.Delete(testRoot, recursive: true);
    }
}

static async Task VerifySuccessfulExtractionAsync(string testRoot)
{
    using MemoryStream archive = CreateArchive();
    string destination = Path.Combine(testRoot, "success");

    await SafeZipExtractor.ExtractAsync(archive, destination);

    Require(
        await File.ReadAllTextAsync(Path.Combine(destination, "manifest.txt")) ==
            "version=1",
        "The manifest was not extracted.");
    Require(
        await File.ReadAllTextAsync(Path.Combine(destination, "data", "payload.txt")) ==
            "ready",
        "The nested payload was not extracted.");
    RequireNoStagingDirectories(testRoot);

    Console.WriteLine("PASS: successful extraction publishes only the complete directory");
}

static async Task VerifyCancellationCleanupAsync(string testRoot)
{
    using var cancellation = new CancellationTokenSource();
    using var archive = new MemoryStream([0]);
    string destination = Path.Combine(testRoot, "cancelled");

    try
    {
        await SafeZipExtractor.ExtractCoreAsync(
            archive,
            destination,
            async (_, stagingDirectory, token) =>
            {
                Directory.CreateDirectory(stagingDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(stagingDirectory, "partial.txt"),
                    "partial",
                    token);
                cancellation.Cancel();
            },
            cancellation.Token);

        throw new InvalidOperationException("Expected cancellation was not observed.");
    }
    catch (OperationCanceledException)
    {
        // Expected. The assertions below verify cleanup.
    }

    Require(!Directory.Exists(destination), "A cancelled extraction was published.");
    RequireNoStagingDirectories(testRoot);

    Console.WriteLine("PASS: cancellation removes partial staging files");
}

static async Task VerifyCorruptArchiveCleanupAsync(string testRoot)
{
    using var archive = new MemoryStream(Encoding.UTF8.GetBytes("not a zip archive"));
    string destination = Path.Combine(testRoot, "corrupt");

    try
    {
        await SafeZipExtractor.ExtractAsync(archive, destination);
        throw new InvalidOperationException("Expected invalid ZIP data was accepted.");
    }
    catch (InvalidDataException)
    {
        // Expected. The assertions below verify cleanup.
    }

    Require(!Directory.Exists(destination), "A corrupt archive was published.");
    RequireNoStagingDirectories(testRoot);

    Console.WriteLine("PASS: corrupt input leaves no destination or staging directory");
}

static async Task VerifyExistingDestinationIsPreservedAsync(string testRoot)
{
    string destination = Path.Combine(testRoot, "existing");
    Directory.CreateDirectory(destination);
    string marker = Path.Combine(destination, "keep.txt");
    await File.WriteAllTextAsync(marker, "keep");
    using MemoryStream archive = CreateArchive();

    try
    {
        await SafeZipExtractor.ExtractAsync(archive, destination);
        throw new InvalidOperationException("An existing destination was overwritten.");
    }
    catch (IOException)
    {
        // Expected. The original destination must remain untouched.
    }

    Require(await File.ReadAllTextAsync(marker) == "keep", "Existing content changed.");
    RequireNoStagingDirectories(testRoot);

    Console.WriteLine("PASS: an existing destination is rejected without modification");
}

static async Task VerifyDestinationRaceIsPreservedAsync(string testRoot)
{
    using var archive = new MemoryStream([0]);
    string destination = Path.Combine(testRoot, "raced");
    string marker = Path.Combine(destination, "keep.txt");

    try
    {
        await SafeZipExtractor.ExtractCoreAsync(
            archive,
            destination,
            async (_, stagingDirectory, token) =>
            {
                Directory.CreateDirectory(stagingDirectory);
                await File.WriteAllTextAsync(
                    Path.Combine(stagingDirectory, "complete.txt"),
                    "complete",
                    token);

                Directory.CreateDirectory(destination);
                await File.WriteAllTextAsync(marker, "keep", token);
            },
            CancellationToken.None);

        throw new InvalidOperationException("A raced destination was overwritten.");
    }
    catch (IOException)
    {
        // Expected. Directory.Move refuses to replace the raced destination.
    }

    Require(await File.ReadAllTextAsync(marker) == "keep", "Raced content changed.");
    RequireNoStagingDirectories(testRoot);

    Console.WriteLine("PASS: a destination created during extraction wins the race safely");
}

static MemoryStream CreateArchive()
{
    var stream = new MemoryStream();
    using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
    {
        WriteEntry(archive, "manifest.txt", "version=1");
        WriteEntry(archive, "data/payload.txt", "ready");
    }

    stream.Position = 0;
    return stream;
}

static void WriteEntry(ZipArchive archive, string name, string content)
{
    ZipArchiveEntry entry = archive.CreateEntry(name, CompressionLevel.NoCompression);
    using StreamWriter writer = new(entry.Open(), Encoding.UTF8);
    writer.Write(content);
}

static void RequireNoStagingDirectories(string parent)
{
    string[] leftovers = Directory.GetDirectories(
        parent,
        ".zip-extract-*.tmp",
        SearchOption.TopDirectoryOnly);
    Require(leftovers.Length == 0, "A staging directory was left behind.");
}

static void Require(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException(message);
    }
}
