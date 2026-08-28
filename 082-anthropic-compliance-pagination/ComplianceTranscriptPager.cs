using System.Globalization;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace ComplianceTranscriptPagination;

public sealed class ComplianceTranscriptPager(HttpClient httpClient)
{
    public async Task<IReadOnlyList<TranscriptMessage>> ReadAllAsync(
        string sessionId,
        int limit = 1_000,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        if (limit is < 1 or > 1_000)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "The transcript page limit must be between 1 and 1,000.");
        }

        var messages = new List<TranscriptMessage>();
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;

        do
        {
            using var response = await httpClient.GetAsync(
                BuildRequestUri(sessionId, limit, cursor),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var page = await response.Content.ReadFromJsonAsync<TranscriptPage>(
                cancellationToken: cancellationToken)
                ?? throw new InvalidDataException("The transcript response body was empty.");

            messages.AddRange(page.Data);

            if (page.NextPage is { } nextPage)
            {
                if (string.IsNullOrWhiteSpace(nextPage))
                {
                    throw new InvalidDataException("next_page must be null or a non-empty cursor.");
                }

                if (!seenCursors.Add(nextPage))
                {
                    throw new InvalidDataException($"The API repeated cursor '{nextPage}'.");
                }
            }

            cursor = page.NextPage;
        }
        while (cursor is not null);

        return messages;
    }

    private static string BuildRequestUri(string sessionId, int limit, string? cursor)
    {
        var requestUri = string.Create(
            CultureInfo.InvariantCulture,
            $"v1/compliance/apps/sessions/local/{Uri.EscapeDataString(sessionId)}/messages?order=asc&limit={limit}");

        return cursor is null
            ? requestUri
            : $"{requestUri}&page={Uri.EscapeDataString(cursor)}";
    }
}

public sealed record TranscriptPage(
    [property: JsonPropertyName("data")] IReadOnlyList<TranscriptMessage> Data,
    [property: JsonPropertyName("next_page")] string? NextPage);

public sealed record TranscriptMessage(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
