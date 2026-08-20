# ASP.NET Core 10 local redirect policy

This sample validates an untrusted post-login `returnUrl` with
`RedirectHttpResult.IsLocalUrl`. Local paths are preserved, while absolute URLs,
network-path references, backslash lookalikes, relative paths, and missing input
fall back to a known dashboard route.

## Problem

A login or access-denied endpoint often receives the next destination from a
query string. Passing that value directly to a normal redirect can turn the app
into an open-redirect hop. Hand-written checks such as `StartsWith('/')` also
accept `//evil.example` and `/\evil.example`, which can be interpreted as an
authority-shaped destination.

## Prerequisites

- .NET 10 SDK
- ASP.NET Core 10 shared framework

The sample was validated with .NET SDK 10.0.303 and ASP.NET Core runtime
10.0.11. It has no package dependencies, credentials, external services, or
runtime network calls.

## Setup and run

From this folder:

```bash
dotnet restore
dotnet build -c Release --no-restore
dotnet run -c Release --no-build
```

The executable runs a table-driven URL matrix and then checks the policy result
for an accepted local path, two hostile inputs, and missing input.

## Deterministic verification

```bash
dotnet format --verify-no-changes
dotnet run -c Release --no-build
dotnet list package --vulnerable --include-transitive
```

The verifier exits with code `0` only when all checks pass. Its last line is:

```text
Verifier: 16/16 checks passed.
```

Expected behavior:

- `/` and `/account/profile?tab=security` are local.
- `~/account/profile` is a supported virtual local path.
- `//evil.example`, `/\evil.example`, absolute HTTP(S) URLs, scheme-like
  values, plain relative paths, and malformed virtual paths are rejected.
- Rejected or missing values become `/dashboard`.
- Every returned `RedirectHttpResult` retains `AcceptLocalUrlOnly=true`.

## Use in an endpoint

Call `LocalRedirectPolicy.AfterLogin(returnUrl)` after authentication succeeds.
The helper chooses a destination before constructing `TypedResults.LocalRedirect`,
which lets the application record or count rejected values without attempting
the redirect.

## Limitations

`IsLocalUrl` answers whether a URL is local according to ASP.NET Core. It does
not prove that the route exists, authorize the caller to view it, or implement
an allowlist for external destinations. If an application intentionally sends
users to another host, use a separate, explicit host-and-scheme allowlist.
