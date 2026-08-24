using McpIssuerValidation;

const string expectedIssuer = "https://auth.example.com";

var cases = new[]
{
    new Case("advertised + exact iss", expectedIssuer, true, expectedIssuer, IssuerValidationDecision.Proceed),
    new Case("advertised + missing iss", expectedIssuer, true, null, IssuerValidationDecision.RejectMissingIssuer),
    new Case("advertised + mismatched iss", expectedIssuer, true, "https://other.example.com", IssuerValidationDecision.RejectIssuerMismatch),
    new Case("not advertised + exact iss", expectedIssuer, false, expectedIssuer, IssuerValidationDecision.Proceed),
    new Case("not advertised + missing iss", expectedIssuer, false, null, IssuerValidationDecision.Proceed),
    new Case("not advertised + mismatched iss", expectedIssuer, false, "https://other.example.com", IssuerValidationDecision.RejectIssuerMismatch),
    new Case("metadata flag absent + exact iss", expectedIssuer, null, expectedIssuer, IssuerValidationDecision.Proceed),
    new Case("metadata flag absent + missing iss", expectedIssuer, null, null, IssuerValidationDecision.Proceed),
    new Case("host case is not normalized", expectedIssuer, null, "https://AUTH.example.com", IssuerValidationDecision.RejectIssuerMismatch),
    new Case("trailing slash is not normalized", expectedIssuer, null, "https://auth.example.com/", IssuerValidationDecision.RejectIssuerMismatch),
    new Case("default port is not normalized", "https://auth.example.com:443", null, expectedIssuer, IssuerValidationDecision.RejectIssuerMismatch),
    new Case("percent encoding is not normalized", "https://auth.example.com/%7Eissuer", null, "https://auth.example.com/~issuer", IssuerValidationDecision.RejectIssuerMismatch),
    new Case("empty iss is present but mismatched", expectedIssuer, false, string.Empty, IssuerValidationDecision.RejectIssuerMismatch)
};

var passed = 0;

foreach (var testCase in cases)
{
    var result = AuthorizationResponseIssuerValidator.Validate(
        testCase.RecordedIssuer,
        testCase.MetadataAdvertisesIssuer,
        testCase.ResponseIssuer);

    Check(result.Decision == testCase.ExpectedDecision, $"{testCase.Name}: decision");
    Check(
        result.MayProcessAuthorizationResponse ==
        (testCase.ExpectedDecision is IssuerValidationDecision.Proceed),
        $"{testCase.Name}: response gate");

    passed++;
    Console.WriteLine($"PASS: {testCase.Name} -> {result.Decision}");
}

Console.WriteLine($"{passed}/{cases.Length} issuer cases passed");

static void Check(bool condition, string name)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {name}");
    }
}

internal sealed record Case(
    string Name,
    string RecordedIssuer,
    bool? MetadataAdvertisesIssuer,
    string? ResponseIssuer,
    IssuerValidationDecision ExpectedDecision);
