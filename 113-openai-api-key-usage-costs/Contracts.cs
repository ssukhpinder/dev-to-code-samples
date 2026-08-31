using System.Text.Json.Serialization;

namespace OpenAiApiKeyUsageCosts;

internal sealed record UsagePage(
    [property: JsonPropertyName("data")] IReadOnlyList<UsageBucket> Data,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("next_page")] string? NextPage);

internal sealed record UsageBucket(
    [property: JsonPropertyName("start_time")] long StartTime,
    [property: JsonPropertyName("end_time")] long EndTime,
    [property: JsonPropertyName("results")] IReadOnlyList<UsageResult> Results);

internal sealed record UsageResult(
    [property: JsonPropertyName("input_tokens")] long InputTokens,
    [property: JsonPropertyName("output_tokens")] long OutputTokens,
    [property: JsonPropertyName("num_model_requests")] long ModelRequests,
    [property: JsonPropertyName("api_key_id")] string? ApiKeyId);

internal sealed record CostPage(
    [property: JsonPropertyName("data")] IReadOnlyList<CostBucket> Data,
    [property: JsonPropertyName("has_more")] bool HasMore,
    [property: JsonPropertyName("next_page")] string? NextPage);

internal sealed record CostBucket(
    [property: JsonPropertyName("start_time")] long StartTime,
    [property: JsonPropertyName("end_time")] long EndTime,
    [property: JsonPropertyName("results")] IReadOnlyList<CostResult> Results);

internal sealed record CostResult(
    [property: JsonPropertyName("amount")] Money Amount,
    [property: JsonPropertyName("api_key_id")] string? ApiKeyId);

internal sealed record Money(
    [property: JsonPropertyName("value")] decimal Value,
    [property: JsonPropertyName("currency")] string Currency);

internal readonly record struct DailyApiKey(long StartTime, long EndTime, string ApiKeyId);

internal enum ReconciliationStatus
{
    Matched,
    UsageOnly,
    CostOnly
}

internal sealed record ReconciliationRow(
    DailyApiKey Key,
    bool HasUsage,
    long InputTokens,
    long OutputTokens,
    long ModelRequests,
    bool HasCost,
    decimal Amount,
    string? Currency,
    ReconciliationStatus Status);
