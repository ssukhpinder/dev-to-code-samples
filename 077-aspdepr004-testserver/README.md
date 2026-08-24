# ASP.NET Core 10 ASPDEPR004 TestServer migration

## Problem

.NET 10 marks the concrete `WebHostBuilder` type obsolete. A legacy in-memory integration-test setup such as `new TestServer(new WebHostBuilder())` now emits `ASPDEPR004`; the old `TestServer(IWebHostBuilder)` constructor also emits the related `ASPDEPR008`. A warnings-as-errors build therefore stops on this hosting boundary.

Suppressing the warning leaves the old hosting model in place. This sample instead migrates the test harness to the generic `HostBuilder`, registers `TestServer` through `UseTestServer()`, starts the host, and obtains the in-memory client from the running host.

The sample keeps an intentional failing probe beside a deterministic migrated verifier so the warning and the replacement can both be checked without opening a network port.

## Prerequisites

- A stable .NET 10 SDK. `global.json` accepts the latest installed .NET 10 feature band and rejects prerelease SDKs.
- NuGet access during restore for `Microsoft.AspNetCore.TestHost` 10.0.11 and during the optional live vulnerability-advisory checks.
- No database, container, browser, account, credential, paid API, Kestrel listener, or external runtime service.

The verifier needs no secret. If an adapted integration test calls an authenticated dependency, inject a fake or supply a placeholder environment variable such as `TEST_API_TOKEN=replace-me`; never commit the real value.

## Setup and validation

Run these commands from this folder:

```powershell
dotnet --version
dotnet restore .\LegacyProbe\LegacyProbe.csproj
dotnet restore .\MigratedVerifier\MigratedVerifier.csproj
dotnet format .\LegacyProbe\LegacyProbe.csproj whitespace --verify-no-changes --no-restore
dotnet format .\MigratedVerifier\MigratedVerifier.csproj whitespace --verify-no-changes --no-restore
dotnet build .\LegacyProbe\LegacyProbe.csproj -c Release --no-restore
dotnet build .\MigratedVerifier\MigratedVerifier.csproj -c Release --no-restore
dotnet run --project .\MigratedVerifier\MigratedVerifier.csproj -c Release --no-build
dotnet list .\LegacyProbe\LegacyProbe.csproj package
dotnet list .\MigratedVerifier\MigratedVerifier.csproj package
dotnet list .\LegacyProbe\LegacyProbe.csproj package --vulnerable --include-transitive
dotnet list .\MigratedVerifier\MigratedVerifier.csproj package --vulnerable --include-transitive
```

The `LegacyProbe` build is the one intentional failure. It must report `ASPDEPR004` as an error for `WebHostBuilder` and `ASPDEPR008` for the old `TestServer` constructor. The migrated project must build with zero warnings and zero errors, and its run ends with:

```text
8/8 migration checks passed
```

The build and verifier run are deterministic and need no runtime network access. The two `--vulnerable` commands are separate live audits whose result depends on the advisory data currently returned by the configured NuGet sources.

## Migration shape

The obsolete setup passes a concrete web-host builder directly to `TestServer`:

```csharp
using var server = new TestServer(
    new WebHostBuilder().Configure(ConfigurePipeline));
```

The replacement makes the test server part of a generic host:

```csharp
using var host = await new HostBuilder()
    .ConfigureWebHost(webHost =>
    {
        webHost
            .UseEnvironment("Testing")
            .UseTestServer()
            .ConfigureServices(ConfigureServices)
            .Configure(ConfigurePipeline);
    })
    .StartAsync();

using var client = host.GetTestClient();
```

`ConfigureWebHost` still exposes `IWebHostBuilder` inside the callback; the obsolete API is the concrete `WebHostBuilder` construction path. Starting the generic host is required before resolving the server or client.

## Expected behavior and limitations

The migrated verifier proves that `TestServer` is registered, sends one in-memory request, resolves a fake service, preserves the `Testing` environment and request path, returns JSON, and shuts the host down cleanly. Repeated runs should print the same eight pass lines and summary.

`TestServer` is a middleware test server, not a transport emulator. It does not reproduce every Kestrel, socket, TLS, HTTP/2, proxy, or transport-header behavior. Keep separate end-to-end tests for boundaries that depend on those details.

The sample uses a small inline pipeline to isolate the hosting migration. A project with an existing `Startup` class can keep `UseStartup<TStartup>()` inside `ConfigureWebHost`; a minimal API application may be better tested with `WebApplicationFactory<TEntryPoint>` instead. The important contract is to remove direct construction of `WebHostBuilder`, start the generic host, and retrieve the client from that host.

Primary references:

- [WebHostBuilder, IWebHost, and WebHost are obsolete](https://learn.microsoft.com/aspnet/core/breaking-changes/10/webhostbuilder-deprecated?view=aspnetcore-10.0)
- [Test ASP.NET Core middleware with TestServer](https://learn.microsoft.com/aspnet/core/test/middleware?view=aspnetcore-10.0)
- [Microsoft.AspNetCore.TestHost 10.0.11](https://www.nuget.org/packages/Microsoft.AspNetCore.TestHost/10.0.11)
