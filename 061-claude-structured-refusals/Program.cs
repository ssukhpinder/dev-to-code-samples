using System.Text.Json;
using System.Text.Json.Serialization;

var checks = 0;

var completed = ClaudeStructuredOutputDecoder.Decode(Fixtures.CompletedResponse);
Check(completed.Status == DecodeStatus.Success, "completed response is accepted");
Check(
    completed.Value is { Action: ReviewAction.Approve, Reason: "Policy checks passed." },
    "completed payload is deserialized");

var refusal = ClaudeStructuredOutputDecoder.Decode(Fixtures.RefusalResponse);
Check(refusal.Status == DecodeStatus.Refusal, "HTTP 200 refusal is classified before payload parsing");
Check(refusal.Value is null, "refusal returns no domain value");

var truncated = ClaudeStructuredOutputDecoder.Decode(Fixtures.TruncatedResponse);
Check(truncated.Status == DecodeStatus.Truncated, "max_tokens is classified before payload parsing");
Check(truncated.Value is null, "truncated response returns no domain value");

var caseVariant = ClaudeStructuredOutputDecoder.Decode(Fixtures.CaseVariantResponse);
Check(caseVariant.Status == DecodeStatus.Success, "enum casing variant is accepted");
Check(caseVariant.Value?.Action == ReviewAction.Approve, "enum maps only to a declared value");

var malformed = ClaudeStructuredOutputDecoder.Decode(Fixtures.MalformedCompletedResponse);
Check(malformed.Status == DecodeStatus.InvalidPayload, "completed malformed JSON is an invalid payload");

var unsupported = ClaudeStructuredOutputDecoder.Decode(Fixtures.UnsupportedStopResponse);
Check(unsupported.Status == DecodeStatus.UnsupportedStopReason, "unknown stop reason fails closed");

var missingText = ClaudeStructuredOutputDecoder.Decode(Fixtures.MissingTextResponse);
Check(missingText.Status == DecodeStatus.InvalidEnvelope, "missing text block is an invalid envelope");

var duplicateStopReason = ClaudeStructuredOutputDecoder.Decode(Fixtures.DuplicateStopReasonResponse);
Check(duplicateStopReason.Status == DecodeStatus.InvalidEnvelope, "duplicate stop_reason is rejected");

Console.WriteLine($"PASS: {checks}/12 checks");

void Check(bool condition, string description)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAIL: {description}");
    }

    checks++;
    Console.WriteLine($"PASS: {description}");
}

internal static class Fixtures
{
    public const string CompletedResponse = """
    {
      "id": "msg_completed",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"approve\",\"reason\":\"Policy checks passed.\"}"
        }
      ],
      "stop_reason": "end_turn",
      "stop_sequence": null
    }
    """;

    public const string RefusalResponse = """
    {
      "id": "msg_refusal",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "I can't complete that request."
        }
      ],
      "stop_reason": "refusal",
      "stop_sequence": null
    }
    """;

    public const string TruncatedResponse = """
    {
      "id": "msg_truncated",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"approve\""
        }
      ],
      "stop_reason": "max_tokens",
      "stop_sequence": null
    }
    """;

    public const string CaseVariantResponse = """
    {
      "id": "msg_case_variant",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"APPROVE\",\"reason\":\"Case differs only.\"}"
        }
      ],
      "stop_reason": "end_turn",
      "stop_sequence": null
    }
    """;

    public const string MalformedCompletedResponse = """
    {
      "id": "msg_malformed",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"approve\""
        }
      ],
      "stop_reason": "end_turn",
      "stop_sequence": null
    }
    """;

    public const string UnsupportedStopResponse = """
    {
      "id": "msg_tool_use",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"approve\",\"reason\":\"This must not be parsed.\"}"
        }
      ],
      "stop_reason": "tool_use",
      "stop_sequence": null
    }
    """;

    public const string MissingTextResponse = """
    {
      "id": "msg_missing_text",
      "type": "message",
      "content": [
        {
          "type": "thinking",
          "thinking": "Fixture data"
        }
      ],
      "stop_reason": "end_turn",
      "stop_sequence": null
    }
    """;

    public const string DuplicateStopReasonResponse = """
    {
      "id": "msg_duplicate_stop",
      "type": "message",
      "content": [
        {
          "type": "text",
          "text": "{\"action\":\"approve\",\"reason\":\"This must not be parsed.\"}"
        }
      ],
      "stop_reason": "end_turn",
      "stop_reason": "refusal",
      "stop_sequence": null
    }
    """;
}

internal enum ReviewAction
{
    Approve,
    Escalate
}

internal enum DecodeStatus
{
    Success,
    Refusal,
    Truncated,
    InvalidEnvelope,
    InvalidPayload,
    UnsupportedStopReason
}

internal sealed record ReviewDecision(ReviewAction Action, string Reason);

internal sealed record DecodeResult(DecodeStatus Status, ReviewDecision? Value, string Message);

internal static class ClaudeStructuredOutputDecoder
{
    private static readonly JsonDocumentOptions EnvelopeOptions = new()
    {
        AllowDuplicateProperties = false
    };

    private static readonly JsonSerializerOptions PayloadOptions = new(JsonSerializerOptions.Strict);

    public static DecodeResult Decode(string responseJson)
    {
        JsonDocument response;

        try
        {
            response = JsonDocument.Parse(responseJson, EnvelopeOptions);
        }
        catch (JsonException)
        {
            return InvalidEnvelope();
        }

        using (response)
        {
            JsonElement root = response.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("stop_reason", out JsonElement stopReasonElement)
                || stopReasonElement.ValueKind != JsonValueKind.String)
            {
                return InvalidEnvelope();
            }

            string stopReason = stopReasonElement.GetString()!;

            switch (stopReason)
            {
                case "refusal":
                    return new(DecodeStatus.Refusal, null, "Claude refused the request.");
                case "max_tokens":
                    return new(
                        DecodeStatus.Truncated,
                        null,
                        "Claude stopped at max_tokens; structured output may be incomplete.");
                case "end_turn":
                    break;
                default:
                    return new(
                        DecodeStatus.UnsupportedStopReason,
                        null,
                        $"Unsupported stop reason: {stopReason}.");
            }

            if (!TryGetTextBlock(root, out string payloadText))
            {
                return InvalidEnvelope();
            }

            ReviewWire? wire;

            try
            {
                wire = JsonSerializer.Deserialize<ReviewWire>(payloadText, PayloadOptions);
            }
            catch (JsonException)
            {
                return new(DecodeStatus.InvalidPayload, null, "Structured payload is invalid.");
            }

            if (wire is null
                || string.IsNullOrWhiteSpace(wire.Action)
                || string.IsNullOrWhiteSpace(wire.Reason)
                || !TryParseAction(wire.Action, out ReviewAction action))
            {
                return new(DecodeStatus.InvalidPayload, null, "Structured payload is invalid.");
            }

            return new(
                DecodeStatus.Success,
                new ReviewDecision(action, wire.Reason),
                "Completed structured output.");
        }
    }

    private static bool TryGetTextBlock(JsonElement root, out string text)
    {
        text = string.Empty;

        if (!root.TryGetProperty("content", out JsonElement content)
            || content.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        foreach (JsonElement block in content.EnumerateArray())
        {
            if (block.ValueKind == JsonValueKind.Object
                && block.TryGetProperty("type", out JsonElement type)
                && type.ValueKind == JsonValueKind.String
                && type.GetString() == "text"
                && block.TryGetProperty("text", out JsonElement textElement)
                && textElement.ValueKind == JsonValueKind.String)
            {
                text = textElement.GetString() ?? string.Empty;
                return !string.IsNullOrWhiteSpace(text);
            }
        }

        return false;
    }

    private static bool TryParseAction(string value, out ReviewAction action)
    {
        if (value.Equals("approve", StringComparison.OrdinalIgnoreCase))
        {
            action = ReviewAction.Approve;
            return true;
        }

        if (value.Equals("escalate", StringComparison.OrdinalIgnoreCase))
        {
            action = ReviewAction.Escalate;
            return true;
        }

        action = default;
        return false;
    }

    private static DecodeResult InvalidEnvelope() =>
        new(DecodeStatus.InvalidEnvelope, null, "Response envelope is invalid.");

    private sealed record ReviewWire
    {
        [JsonPropertyName("action")]
        public required string Action { get; init; }

        [JsonPropertyName("reason")]
        public required string Reason { get; init; }
    }
}
