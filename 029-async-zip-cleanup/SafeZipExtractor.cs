using System.IO.Compression;

namespace AsyncZipCleanup;

public static class SafeZipExtractor
{
    public static Task ExtractAsync(
        Stream archive,
        string destinationDirectory,
        CancellationToken cancellationToken = default) =>
        ExtractCoreAsync(
            archive,
            destinationDirectory,
            static (source, stagingDirectory, token) =>
                ZipFile.ExtractToDirectoryAsync(
                    source,
                    stagingDirectory,
                    overwriteFiles: false,
                    token),
            cancellationToken);

    internal static async Task ExtractCoreAsync(
        Stream archive,
        string destinationDirectory,
        Func<Stream, string, CancellationToken, Task> extract,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(archive);
        ArgumentException.ThrowIfNullOrWhiteSpace(destinationDirectory);
        ArgumentNullException.ThrowIfNull(extract);

        string destination = Path.GetFullPath(destinationDirectory);
        string destinationName = Path.GetFileName(
            destination.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

        if (destinationName.Length == 0)
        {
            throw new ArgumentException(
                "The destination must name a directory below a parent path.",
                nameof(destinationDirectory));
        }

        if (Directory.Exists(destination) || File.Exists(destination))
        {
            throw new IOException($"The destination already exists: {destination}");
        }

        cancellationToken.ThrowIfCancellationRequested();

        string parent = Path.GetDirectoryName(destination)
            ?? throw new ArgumentException(
                "The destination must have a parent directory.",
                nameof(destinationDirectory));

        Directory.CreateDirectory(parent);
        string stagingDirectory = Path.Combine(
            parent,
            $".zip-extract-{Guid.NewGuid():N}.tmp");

        try
        {
            await extract(archive, stagingDirectory, cancellationToken)
                .ConfigureAwait(false);

            cancellationToken.ThrowIfCancellationRequested();
            Directory.Move(stagingDirectory, destination);
        }
        catch (Exception operationException)
        {
            try
            {
                if (Directory.Exists(stagingDirectory))
                {
                    Directory.Delete(stagingDirectory, recursive: true);
                }
            }
            catch (Exception cleanupException)
            {
                throw new AggregateException(
                    $"ZIP extraction failed and staging cleanup also failed: {stagingDirectory}",
                    operationException,
                    cleanupException);
            }

            throw;
        }
    }
}
