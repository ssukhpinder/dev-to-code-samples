# ASP.NET Core 10 Authentication Metrics: No Result vs Failure

An HTTP 401 alone cannot tell me whether a caller omitted credentials or supplied credentials that failed validation. ASP.NET Core 10 exposes that distinction through the `Microsoft.AspNetCore.Authentication` meter.

This sample starts an in-memory ASP.NET Core app, sends three deterministic requests, and captures the built-in metrics with `MeterListener`:

- with no credential, the fake handler records `aspnetcore.authentication.result=none`; authorization then invokes a challenge and the protected endpoint returns 401;
- with a rejected credential, the handler records `failure`; authorization separately invokes another challenge and the endpoint returns the same 401;
- an accepted credential on the protected endpoint produces `success`;
- all three captured duration measurements include the fixed `DemoToken` scheme;
- the failed measurement exposes `error.type=System.InvalidOperationException`, while neither credential nor the exception message appears in any attribute.

The custom handler is deliberately a fake. It exists to exercise the framework's metrics offline, not to model production authentication.

## Prerequisites

- .NET 10 SDK with ASP.NET Core runtime 10.0.11 or later
- network access for the initial NuGet restore

No API key, account, external identity provider, or paid service is required. A production app should use its supported cookie, bearer, certificate, or remote authentication handler; do not copy the fake credential check.

## Setup and run

From this folder:

```bash
dotnet restore AuthenticationMetrics.csproj
dotnet run --project AuthenticationMetrics.csproj --configuration Release
```

Expected behavior:

```text
PASS: authentication results include none, failure, and success.
PASS: missing and rejected credentials both returned 401 and produced two challenges.
PASS: attributes expose the exception type, not credentials or the exception message.
```

The process exits nonzero if an HTTP result or metric assertion differs.

## Deterministic verification

```bash
dotnet format AuthenticationMetrics.csproj --verify-no-changes --no-restore
dotnet build AuthenticationMetrics.csproj --configuration Release --no-restore
dotnet run --project AuthenticationMetrics.csproj --configuration Release --no-build
dotnet list AuthenticationMetrics.csproj package --include-transitive
dotnet list AuthenticationMetrics.csproj package --vulnerable --include-transitive
```

The sample uses `Microsoft.AspNetCore.TestHost` 10.0.11 so it never opens a socket. It measures framework behavior, not latency: the three duration values are intentionally not asserted because timing varies by machine.

## Limitations

`MeterListener` is useful for a focused regression harness. For production storage and alerting, export the `Microsoft.AspNetCore.Authentication` meter through OpenTelemetry or another metrics pipeline. Aggregate on bounded attributes such as scheme and result. Do not add user IDs, credential material, raw tokens, or exception messages as metric attributes; those values create privacy and cardinality problems.

Article: _ASP.NET Core 10 Authentication Metrics: Distinguish No Result from Failure_ (draft)
