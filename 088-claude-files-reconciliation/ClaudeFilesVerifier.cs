using System.Text.Json;
using System.Text.Json.Serialization;

namespace ClaudeFilesReconciliation;

internal static class ClaudeFilesVerifier
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static Uri BuildListUri(IReadOnlyList<string> requestedIds)
    {
        ArgumentNullException.ThrowIfNull(requestedIds);

        if (requestedIds.Count is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(
                nameof(requestedIds),
                "The Files API accepts between 1 and 100 ids[] values.");
        }

        if (requestedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("File IDs cannot be empty.", nameof(requestedIds));
        }

        var uniqueIds = new HashSet<string>(requestedIds, StringComparer.Ordinal);
        if (uniqueIds.Count != requestedIds.Count)
        {
            throw new ArgumentException(
                "De-duplicate requested IDs before reconciliation.",
                nameof(requestedIds));
        }

        var query = string.Join(
            '&',
            requestedIds.Select(id => $"ids%5B%5D={Uri.EscapeDataString(id)}"));

        return new Uri($"/v1/files?{query}", UriKind.Relative);
    }

    public static async Task<FileReconciliation> ReconcileAsync(
        HttpClient client,
        IReadOnlyList<string> requestedIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(client);

        using var response = await client.GetAsync(
            BuildListUri(requestedIds),
            cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var responseBody = await response.Content.ReadAsStreamAsync(cancellationToken);
        var payload = await JsonSerializer.DeserializeAsync<FileListResponse>(
            responseBody,
            JsonOptions,
            cancellationToken)
            ?? throw new InvalidDataException("The Files API returned an empty response body.");

        if (payload.NextPage is not null)
        {
            throw new InvalidDataException("An ids[] response must have next_page set to null.");
        }

        var returnedIds = (payload.Data
                ?? throw new InvalidDataException("The Files API response omitted data."))
            .Select(file => file.Id)
            .ToArray();

        if (returnedIds.Any(string.IsNullOrWhiteSpace))
        {
            throw new InvalidDataException("The Files API returned an empty file ID.");
        }

        var requestedSet = new HashSet<string>(requestedIds, StringComparer.Ordinal);
        var returnedSet = new HashSet<string>(returnedIds!, StringComparer.Ordinal);

        var missing = requestedIds
            .Where(id => !returnedSet.Contains(id))
            .ToArray();
        var unexpected = returnedIds
            .Where(id => !requestedSet.Contains(id!))
            .Select(id => id!)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var duplicates = returnedIds
            .GroupBy(id => id!, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        return new FileReconciliation(
            returnedIds.Select(id => id!).ToArray(),
            missing,
            unexpected,
            duplicates);
    }
}

internal sealed record FileReconciliation(
    IReadOnlyList<string> Returned,
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Unexpected,
    IReadOnlyList<string> Duplicates)
{
    public bool IsComplete =>
        Missing.Count == 0 &&
        Unexpected.Count == 0 &&
        Duplicates.Count == 0;
}

internal sealed record FileListResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<FileMetadata>? Data,
    [property: JsonPropertyName("next_page")] string? NextPage);

internal sealed record FileMetadata(
    [property: JsonPropertyName("id")] string? Id);
