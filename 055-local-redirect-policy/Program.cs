using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;

UrlCase[] urlCases =
[
    new("root path", "/", true),
    new("absolute app path", "/account/profile?tab=security", true),
    new("virtual app path", "~/account/profile", true),
    new("network-path reference", "//evil.example/phish", false),
    new("backslash authority lookalike", @"/\evil.example\phish", false),
    new("HTTPS URL", "https://evil.example/phish", false),
    new("HTTP URL", "http://evil.example/phish", false),
    new("scheme-like value", "javascript:alert(1)", false),
    new("relative path", "account/profile", false),
    new("empty value", string.Empty, false),
    new("virtual network path", "~//evil.example/phish", false),
    new("virtual backslash lookalike", @"~/\evil.example\phish", false),
];

var passed = 0;
var total = 0;

foreach (UrlCase urlCase in urlCases)
{
    bool actual = RedirectHttpResult.IsLocalUrl(urlCase.Value);
    Verify(
        actual == urlCase.Expected,
        $"{urlCase.Name}: expected local={urlCase.Expected}, actual={actual}");
}

RedirectHttpResult accepted = LocalRedirectPolicy.AfterLogin("/orders/42?tab=history");
Verify(
    accepted.Url == "/orders/42?tab=history" && accepted.AcceptLocalUrlOnly,
    "accepted local returnUrl is preserved and remains local-only");

RedirectHttpResult rejectedAbsolute = LocalRedirectPolicy.AfterLogin(
    "https://evil.example/collect");
Verify(
    rejectedAbsolute.Url == LocalRedirectPolicy.SafeFallback &&
    rejectedAbsolute.AcceptLocalUrlOnly,
    "absolute returnUrl falls back to the dashboard");

RedirectHttpResult rejectedNetworkPath = LocalRedirectPolicy.AfterLogin(
    "//evil.example/collect");
Verify(
    rejectedNetworkPath.Url == LocalRedirectPolicy.SafeFallback &&
    rejectedNetworkPath.AcceptLocalUrlOnly,
    "network-path returnUrl falls back to the dashboard");

RedirectHttpResult missing = LocalRedirectPolicy.AfterLogin(null);
Verify(
    missing.Url == LocalRedirectPolicy.SafeFallback && missing.AcceptLocalUrlOnly,
    "missing returnUrl falls back to the dashboard");

Console.WriteLine($"Verifier: {passed}/{total} checks passed.");
return passed == total ? 0 : 1;

void Verify(bool condition, string message)
{
    total++;

    if (condition)
    {
        passed++;
        Console.WriteLine($"PASS {total:00}: {message}");
        return;
    }

    Console.Error.WriteLine($"FAIL {total:00}: {message}");
}

internal static class LocalRedirectPolicy
{
    public const string SafeFallback = "/dashboard";

    public static RedirectHttpResult AfterLogin(string? returnUrl)
    {
        string destination = returnUrl is not null &&
            RedirectHttpResult.IsLocalUrl(returnUrl)
                ? returnUrl
                : SafeFallback;

        return TypedResults.LocalRedirect(destination);
    }
}

internal sealed record UrlCase(string Name, string Value, bool Expected);
