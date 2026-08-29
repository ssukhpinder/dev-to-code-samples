using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

var fixturePepper = Convert.FromHexString(
    "00112233445566778899AABBCCDDEEFF00112233445566778899AABBCCDDEEFF");

var first = ResponsesRequestFactory.Create(
    subject: "person@example.test",
    fixturePepper,
    cacheGroup: "support-flow",
    cacheVersion: "v3",
    input: "Summarize the ticket.");

var sameUser = ResponsesRequestFactory.Create(
    subject: "person@example.test",
    fixturePepper,
    cacheGroup: "support-flow",
    cacheVersion: "v3",
    input: "Classify the ticket.");

var secondUser = ResponsesRequestFactory.Create(
    subject: "another@example.test",
    fixturePepper,
    cacheGroup: "support-flow",
    cacheVersion: "v3",
    input: "Summarize another ticket.");

var nextPromptVersion = ResponsesRequestFactory.Create(
    subject: "person@example.test",
    fixturePepper,
    cacheGroup: "support-flow",
    cacheVersion: "v4",
    input: "Summarize the ticket.");

var json = ResponsesRequestFactory.Serialize(first);
using var document = JsonDocument.Parse(json);
var root = document.RootElement;
var checks = new CheckRunner();

checks.That(
    !root.TryGetProperty("user", out _),
    "deprecated user field is absent");
checks.That(
    root.GetProperty("safety_identifier").GetString() == first.SafetyIdentifier,
    "safety_identifier is serialized");
checks.That(
    root.GetProperty("prompt_cache_key").GetString() == "12_support-flow_2_v3",
    "prompt_cache_key names the reusable workflow");
checks.That(
    first.SafetyIdentifier.Length == 64 && first.SafetyIdentifier.All(IsLowerHex),
    "safety identifier is a 64-character lowercase digest");
checks.That(
    !json.Contains("person@example.test", StringComparison.Ordinal),
    "raw user identity is absent from JSON");
checks.That(
    first.SafetyIdentifier == sameUser.SafetyIdentifier,
    "same user keeps one stable safety identifier");
checks.That(
    first.SafetyIdentifier != secondUser.SafetyIdentifier,
    "different users get different safety identifiers");
checks.That(
    first.PromptCacheKey == secondUser.PromptCacheKey,
    "cache grouping stays independent from user identity");
checks.That(
    first.PromptCacheKey != nextPromptVersion.PromptCacheKey,
    "prompt version changes the cache key");
checks.Throws<ArgumentException>(
    () => ResponsesRequestFactory.Create(
        "person@example.test",
        new byte[16],
        "support-flow",
        "v3",
        "Summarize the ticket."),
    "short privacy pepper fails before transport");
var firstCollisionCandidate = ResponsesRequestFactory.Create(
    "person@example.test",
    fixturePepper,
    "a-b",
    "c",
    "Summarize the ticket.");
var secondCollisionCandidate = ResponsesRequestFactory.Create(
    "person@example.test",
    fixturePepper,
    "a",
    "b-c",
    "Summarize the ticket.");
checks.That(
    firstCollisionCandidate.PromptCacheKey != secondCollisionCandidate.PromptCacheKey,
    "cache components cannot collide at their boundary");
checks.Throws<ArgumentException>(
    () => ResponsesRequestFactory.Create(
        "person@example.test",
        fixturePepper,
        new string('a', 60),
        "v3",
        "Summarize the ticket."),
    "overlong prompt cache key fails before transport");
checks.Throws<ArgumentException>(
    () => ResponsesRequestFactory.Create(
        "person@example.test",
        fixturePepper,
        "support:flow",
        "v3",
        "Summarize the ticket."),
    "unsupported cache component characters fail before transport");
checks.That(
    json == ResponsesRequestFactory.Serialize(first),
    "payload serialization is byte-identical");

Console.WriteLine(json);
Console.WriteLine($"payload-sha256={Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)))}");
checks.Finish();

static bool IsLowerHex(char value) =>
    value is >= '0' and <= '9' or >= 'a' and <= 'f';

internal static class ResponsesRequestFactory
{
    public static ResponsesRequest Create(
        string subject,
        ReadOnlySpan<byte> privacyPepper,
        string cacheGroup,
        string cacheVersion,
        string input)
    {
        RequireCanonicalValue(subject, nameof(subject));
        RequireCacheKeyComponent(cacheGroup, nameof(cacheGroup));
        RequireCacheKeyComponent(cacheVersion, nameof(cacheVersion));

        if (string.IsNullOrWhiteSpace(input))
        {
            throw new ArgumentException("Input is required.", nameof(input));
        }

        if (privacyPepper.Length < 32)
        {
            throw new ArgumentException(
                "Use at least 32 bytes from a secret manager.",
                nameof(privacyPepper));
        }

        var subjectBytes = Encoding.UTF8.GetBytes(subject);
        var safetyIdentifier = Convert.ToHexString(
            HMACSHA256.HashData(privacyPepper, subjectBytes)).ToLowerInvariant();
        var promptCacheKey =
            $"{cacheGroup.Length}_{cacheGroup}_{cacheVersion.Length}_{cacheVersion}";

        if (promptCacheKey.Length > 64)
        {
            throw new ArgumentException(
                "The composed prompt cache key cannot exceed 64 characters.",
                nameof(cacheGroup));
        }

        return new ResponsesRequest(
            Model: "your-model",
            Input: input,
            SafetyIdentifier: safetyIdentifier,
            PromptCacheKey: promptCacheKey);
    }

    public static string Serialize(ResponsesRequest request) =>
        JsonSerializer.Serialize(request, ResponsesJsonContext.Default.ResponsesRequest);

    private static void RequireCanonicalValue(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim())
        {
            throw new ArgumentException(
                "A nonblank canonical value without surrounding whitespace is required.",
                parameterName);
        }
    }

    private static void RequireCacheKeyComponent(string value, string parameterName)
    {
        RequireCanonicalValue(value, parameterName);
        if (!value.All(IsCacheKeyCharacter))
        {
            throw new ArgumentException(
                "Use only ASCII letters, digits, underscores, and hyphens.",
                parameterName);
        }
    }

    private static bool IsCacheKeyCharacter(char value) =>
        value is >= 'a' and <= 'z'
            or >= 'A' and <= 'Z'
            or >= '0' and <= '9'
            or '_'
            or '-';
}

internal sealed record ResponsesRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("input")] string Input,
    [property: JsonPropertyName("safety_identifier")] string SafetyIdentifier,
    [property: JsonPropertyName("prompt_cache_key")] string PromptCacheKey);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(ResponsesRequest))]
internal sealed partial class ResponsesJsonContext : JsonSerializerContext;

internal sealed class CheckRunner
{
    private int passed;
    private int total;

    public void That(bool condition, string name)
    {
        total++;
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL {name}");
        }

        passed++;
        Console.WriteLine($"PASS {name}");
    }

    public void Throws<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
        }
        catch (TException)
        {
            That(true, name);
            return;
        }

        That(false, name);
    }

    public void Finish()
    {
        Console.WriteLine($"{passed}/{total} checks passed");
        if (passed != total)
        {
            Environment.ExitCode = 1;
        }
    }
}
