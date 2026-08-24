using System.Security.Claims;

const string ExpectedAuthenticationType = "Bearer";
const string RequiredScope = "tools.admin";

Check[] checks =
[
    new(
        "rejects an anonymous spoof of a trusted name",
        Principal(authenticationType: null),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
    new(
        "rejects a low-scope spoof of a trusted name",
        Principal(ExpectedAuthenticationType, "tools.read"),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
    new(
        "allows a scoped principal with an unfamiliar name",
        Principal(ExpectedAuthenticationType, RequiredScope),
        new ClientInfo("unfamiliar-client", "1.0.0"),
        Expected: true),
    new(
        "allows a scoped principal when clientInfo is absent",
        Principal(ExpectedAuthenticationType, RequiredScope),
        ClientInfo: null,
        Expected: true),
    new(
        "keeps authorization stable when the reported name changes",
        Principal(ExpectedAuthenticationType, RequiredScope),
        new ClientInfo("renamed-client", "2.0.0"),
        Expected: true),
    new(
        "finds the required scope in a space-delimited claim",
        Principal(ExpectedAuthenticationType, $"tools.read  {RequiredScope}"),
        new ClientInfo("browser-client", "3.1.4"),
        Expected: true),
    new(
        "compares scope values with ordinal case sensitivity",
        Principal(ExpectedAuthenticationType, "TOOLS.ADMIN"),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
    new(
        "does not treat a tab as an OAuth scope separator",
        Principal(ExpectedAuthenticationType, $"tools.read\t{RequiredScope}"),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
    new(
        "rejects a scope from the wrong authentication type",
        Principal("Cookies", RequiredScope),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
    new(
        "rejects a scope from an unauthenticated secondary identity",
        MixedPrincipal(ExpectedAuthenticationType, RequiredScope),
        new ClientInfo("trusted-cli", "9.9.9"),
        Expected: false),
];

foreach (Check check in checks)
{
    // clientInfo is deliberately present on the request fixture but absent from
    // the authorization method's inputs.
    var request = new ToolRequest(check.ClientInfo);

    AuthorizationDecision decision = ToolAuthorizer.Authorize(
        check.Principal,
        ExpectedAuthenticationType,
        RequiredScope);

    Assert.Equal(check.Expected, decision.Allowed, check.Name);
    Assert.Equal(check.ClientInfo, request.ClientInfo, $"{check.Name}: request metadata");

    Console.WriteLine($"PASS: {check.Name}");
}

Console.WriteLine($"{checks.Length}/{checks.Length} checks passed");
return;

static ClaimsPrincipal Principal(string? authenticationType, params string[] scopeClaims)
{
    IEnumerable<Claim> claims = scopeClaims.Select(value => new Claim("scope", value));
    var identity = new ClaimsIdentity(claims, authenticationType);

    return new ClaimsPrincipal(identity);
}

static ClaimsPrincipal MixedPrincipal(
    string authenticatedType,
    string unauthenticatedScope)
{
    var authenticatedIdentity = new ClaimsIdentity(
        claims: [],
        authenticationType: authenticatedType);
    var unauthenticatedIdentity = new ClaimsIdentity(
        claims: [new Claim("scope", unauthenticatedScope)]);

    return new ClaimsPrincipal([authenticatedIdentity, unauthenticatedIdentity]);
}

internal sealed record ClientInfo(string Name, string Version);

internal sealed record ToolRequest(ClientInfo? ClientInfo);

internal sealed record Check(
    string Name,
    ClaimsPrincipal Principal,
    ClientInfo? ClientInfo,
    bool Expected);

internal sealed record AuthorizationDecision(bool Allowed, string Reason);

internal static class ToolAuthorizer
{
    public static AuthorizationDecision Authorize(
        ClaimsPrincipal principal,
        string expectedAuthenticationType,
        string requiredScope)
    {
        ArgumentNullException.ThrowIfNull(principal);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedAuthenticationType);

        if (!IsScopeToken(requiredScope))
        {
            throw new ArgumentException(
                "The required scope is not a valid OAuth scope-token.",
                nameof(requiredScope));
        }

        ClaimsIdentity[] matchingIdentities = principal.Identities
            .Where(identity =>
                identity.IsAuthenticated &&
                string.Equals(
                    identity.AuthenticationType,
                    expectedAuthenticationType,
                    StringComparison.Ordinal))
            .Take(2)
            .ToArray();

        if (matchingIdentities.Length != 1)
        {
            return new(false, "Exactly one identity from the configured authentication type is required.");
        }

        bool hasScope = matchingIdentities[0]
            .FindAll("scope")
            .SelectMany(claim => claim.Value.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries))
            .Any(scope => string.Equals(scope, requiredScope, StringComparison.Ordinal));

        return hasScope
            ? new(true, $"Authenticated identity has scope '{requiredScope}'.")
            : new(false, $"Authenticated identity lacks scope '{requiredScope}'.");
    }

    private static bool IsScopeToken(string value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return false;
        }

        return value.All(character =>
            character == '\u0021' ||
            character is >= '\u0023' and <= '\u005B' ||
            character is >= '\u005D' and <= '\u007E');
    }
}

internal static class Assert
{
    public static void Equal<T>(T expected, T actual, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(expected, actual))
        {
            throw new InvalidOperationException(
                $"{context}: expected {expected}, actual {actual}.");
        }
    }
}
