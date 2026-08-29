using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

const string Policy = "Answer in JSON with keys summary and risks.";
const string ReplacementPolicy = "Answer in one sentence and name one risk.";

var firstTurn = ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
    Model: "gpt-5.6",
    Input: "Review the deployment plan.",
    Instructions: Policy));

var chainedTurn = ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
    Model: "gpt-5.6",
    Input: "Now focus on rollback.",
    Instructions: Policy,
    PreviousResponseId: "resp_fixture_001"));

var replacementTurn = ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
    Model: "gpt-5.6",
    Input: "Give me the short version.",
    Instructions: ReplacementPolicy,
    PreviousResponseId: "resp_fixture_002"));

var checks = new VerificationSuite();

checks.Check("first turn carries instructions", () =>
{
    using var payload = JsonDocument.Parse(firstTurn);
    Require(payload.RootElement.GetProperty("instructions").GetString() == Policy);
    Require(payload.RootElement.GetProperty("store").GetBoolean());
    Require(!payload.RootElement.TryGetProperty("previous_response_id", out _));
});

checks.Check("chained turn repeats instructions", () =>
{
    using var payload = JsonDocument.Parse(chainedTurn);
    Require(payload.RootElement.GetProperty("instructions").GetString() == Policy);
    Require(payload.RootElement.GetProperty("previous_response_id").GetString() == "resp_fixture_001");
});

checks.Check("chained turn can replace instructions deliberately", () =>
{
    using var payload = JsonDocument.Parse(replacementTurn);
    Require(payload.RootElement.GetProperty("instructions").GetString() == ReplacementPolicy);
});

checks.Check("blank instructions fail before transport", () =>
{
    ExpectThrows<ArgumentException>(() => ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
        Model: "gpt-5.6",
        Input: "Continue.",
        Instructions: " ",
        PreviousResponseId: "resp_fixture_003")));
});

checks.Check("blank response IDs fail before transport", () =>
{
    ExpectThrows<ArgumentException>(() => ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
        Model: "gpt-5.6",
        Input: "Continue.",
        Instructions: Policy,
        PreviousResponseId: " ")));
});

checks.Check("previous response and conversation are mutually exclusive", () =>
{
    ExpectThrows<InvalidOperationException>(() => ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
        Model: "gpt-5.6",
        Input: "Continue.",
        Instructions: Policy,
        PreviousResponseId: "resp_fixture_004",
        ConversationId: "conv_fixture_001")));
});

checks.Check("payload key order and content stay deterministic", () =>
{
    var repeated = ResponsesRequestBuilder.BuildJson(new ResponsesTurn(
        Model: "gpt-5.6",
        Input: "Now focus on rollback.",
        Instructions: Policy,
        PreviousResponseId: "resp_fixture_001"));

    Require(repeated == chainedTurn);
});

var fingerprintSource = string.Join('\n', firstTurn, chainedTurn, replacementTurn);
var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(fingerprintSource)));

Console.WriteLine($"payload-sha256={fingerprint}");
Console.WriteLine($"{checks.Passed}/{checks.Total} checks passed");

return checks.Passed == checks.Total ? 0 : 1;

static void Require(bool condition)
{
    if (!condition)
    {
        throw new InvalidOperationException("Verification condition was false.");
    }
}

static void ExpectThrows<TException>(Action action)
    where TException : Exception
{
    try
    {
        action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

internal sealed record ResponsesTurn(
    string Model,
    string Input,
    string Instructions,
    string? PreviousResponseId = null,
    string? ConversationId = null);

internal static class ResponsesRequestBuilder
{
    public static string BuildJson(ResponsesTurn turn)
    {
        ArgumentNullException.ThrowIfNull(turn);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.Model);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.Input);
        ArgumentException.ThrowIfNullOrWhiteSpace(turn.Instructions);

        ValidateOptionalId(turn.PreviousResponseId, nameof(turn.PreviousResponseId));
        ValidateOptionalId(turn.ConversationId, nameof(turn.ConversationId));

        if (turn.PreviousResponseId is not null && turn.ConversationId is not null)
        {
            throw new InvalidOperationException(
                "previous_response_id and conversation cannot be sent together.");
        }

        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("model", turn.Model);
            writer.WriteString("instructions", turn.Instructions);
            writer.WriteString("input", turn.Input);
            writer.WriteBoolean("store", true);

            if (turn.PreviousResponseId is not null)
            {
                writer.WriteString("previous_response_id", turn.PreviousResponseId);
            }

            if (turn.ConversationId is not null)
            {
                writer.WriteString("conversation", turn.ConversationId);
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void ValidateOptionalId(string? value, string parameterName)
    {
        if (value is not null && string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("Identifier cannot be blank.", parameterName);
        }
    }
}

internal sealed class VerificationSuite
{
    public int Passed { get; private set; }

    public int Total { get; private set; }

    public void Check(string name, Action assertion)
    {
        Total++;

        try
        {
            assertion();
            Passed++;
            Console.WriteLine($"PASS {name}");
        }
        catch (Exception exception)
        {
            Console.Error.WriteLine($"FAIL {name}: {exception.Message}");
        }
    }
}
