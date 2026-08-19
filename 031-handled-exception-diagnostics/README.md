# ASP.NET Core 10 Handled Exception Diagnostics

ASP.NET Core 10 suppresses the exception-handler middleware's built-in diagnostics when a registered `IExceptionHandler` returns `true`. That default is useful for routine, expected failures, but it can also hide telemetry for failures that were converted into a safe HTTP response and still need investigation.

This sample uses `ExceptionHandlerOptions.SuppressDiagnosticsCallback` as an explicit policy:

- an expected order conflict returns `409` and suppresses the middleware's `UnhandledException` log;
- a dependency failure returns `503` and keeps that built-in log;
- both response bodies stay deliberately free of exception details.

The verifier starts Kestrel on an ephemeral loopback port, sends both requests, captures logs in memory, and exits nonzero if a status code or diagnostic count differs. It makes no external network or paid API calls.

## Prerequisites

- .NET 10 SDK
- No credentials or external services

## Restore and run

```bash
dotnet restore HandledExceptionDiagnostics.csproj --nologo
dotnet run --project HandledExceptionDiagnostics.csproj --configuration Release
```

Expected output:

```text
PASS: the handler produced safe responses for both failures.
PASS: expected conflict suppressed built-in exception diagnostics.
PASS: dependency failure retained built-in exception diagnostics.
```

## Deterministic verification

```bash
dotnet format HandledExceptionDiagnostics.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build HandledExceptionDiagnostics.csproj --configuration Release --no-restore --nologo
dotnet run --project HandledExceptionDiagnostics.csproj --configuration Release --no-build
dotnet list HandledExceptionDiagnostics.csproj package --include-transitive
dotnet list HandledExceptionDiagnostics.csproj package --vulnerable --include-transitive
```

The project has no package references. The verifier's only HTTP traffic is to the process-local Kestrel listener.

## Limitations

The sample verifies the middleware's `UnhandledException` log because it is easy to assert offline. The same callback also controls the middleware's `HandledException` DiagnosticSource event and the `error.type` tag on `http.server.request.duration`. It does not suppress logs written explicitly inside an `IExceptionHandler`, and unhandled exceptions or exceptions thrown after the response starts still follow their normal diagnostic path.
