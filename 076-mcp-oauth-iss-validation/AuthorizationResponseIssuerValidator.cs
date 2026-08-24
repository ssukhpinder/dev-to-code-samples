namespace McpIssuerValidation;

public enum IssuerValidationDecision
{
    Proceed,
    RejectMissingIssuer,
    RejectIssuerMismatch
}

public readonly record struct IssuerValidationResult(IssuerValidationDecision Decision)
{
    public bool MayProcessAuthorizationResponse => Decision is IssuerValidationDecision.Proceed;
}

public static class AuthorizationResponseIssuerValidator
{
    public static IssuerValidationResult Validate(
        string recordedIssuer,
        bool? authorizationResponseIssuerParameterSupported,
        string? responseIssuer)
    {
        ArgumentException.ThrowIfNullOrEmpty(recordedIssuer);

        if (responseIssuer is null)
        {
            return authorizationResponseIssuerParameterSupported is true
                ? new(IssuerValidationDecision.RejectMissingIssuer)
                : new(IssuerValidationDecision.Proceed);
        }

        return string.Equals(recordedIssuer, responseIssuer, StringComparison.Ordinal)
            ? new(IssuerValidationDecision.Proceed)
            : new(IssuerValidationDecision.RejectIssuerMismatch);
    }
}
