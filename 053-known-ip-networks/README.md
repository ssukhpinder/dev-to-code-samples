# ASP.NET Core 10 KnownIPNetworks

## Problem

ASP.NET Core 10 marks `ForwardedHeadersOptions.KnownNetworks` and the old ASP.NET Core `IPNetwork` type obsolete with warning `ASPDEPR005`. A mechanical rename is not enough: the replacement still defines a trust boundary, and accepting forwarded headers from the wrong subnet lets an untrusted hop influence the client IP address and request scheme seen by later middleware.

This sample uses `System.Net.IPNetwork` with `KnownIPNetworks`, invokes the real forwarded headers middleware, and proves that a proxy inside `10.20.0.0/16` is accepted while an address in the adjacent `10.21.0.0/16` is rejected.

## Prerequisites

- .NET 10 SDK with the ASP.NET Core 10 shared framework
- No NuGet package, credential, proxy, network service, environment variable, or secret is required
- The addresses are documentation-only fixtures; replace them with your proxy CIDRs in a real application

Microsoft documents the [.NET 10 migration to `KnownIPNetworks`](https://learn.microsoft.com/aspnet/core/breaking-changes/10/ipnetwork-knownnetworks-obsolete?view=aspnetcore-10.0) and the [forwarded headers trust settings](https://learn.microsoft.com/aspnet/core/host-and-deploy/proxy-load-balancer?view=aspnetcore-10.0#forwarded-headers-middleware-options).

## Setup and verification

From this folder, run:

```powershell
dotnet restore
dotnet format KnownIpNetworks.csproj --verify-no-changes --no-restore
dotnet build KnownIpNetworks.csproj -c Release --no-restore
dotnet run --project KnownIpNetworks.csproj -c Release --no-build
dotnet list KnownIpNetworks.csproj package --include-transitive
dotnet list KnownIpNetworks.csproj package --vulnerable --include-transitive
```

The console program is the deterministic verifier. A successful run prints:

```text
PASS trusted subnet updates RemoteIpAddress
PASS trusted subnet updates the request scheme
PASS trusted subnet consumes X-Forwarded-For
PASS trusted subnet records the original proxy
PASS adjacent subnet leaves RemoteIpAddress unchanged
PASS adjacent subnet leaves the request scheme unchanged
PASS adjacent subnet keeps X-Forwarded-For
PASS adjacent subnet does not create X-Original-For
8/8 checks passed.
```

The project treats compiler warnings as errors, so bringing back the obsolete `KnownNetworks` API fails the build with `ASPDEPR005`. The verifier uses `DefaultHttpContext` and calls `ForwardedHeadersMiddleware` directly. It opens no port, makes no request, reads no clock, and uses no randomness.

## What the boundary proves

For the trusted proxy, the middleware consumes `X-Forwarded-For`, moves the transport peer into `X-Original-For`, changes `RemoteIpAddress` to the forwarded client, and applies `X-Forwarded-Proto`. The same headers from the adjacent subnet remain untouched, so downstream authentication, redirects, logging, and policy code see the transport values instead of attacker-controlled values.

`ForwardLimit = 1` keeps this fixture scoped to one proxy hop. A real proxy chain needs a deliberate limit and a trusted entry for every hop whose headers the application should consume.

## Limitations

The sample verifies one IPv4 CIDR and one forwarded hop. Production servers can expose IPv4 peers as IPv4-mapped IPv6 addresses, so inspect `HttpContext.Connection.RemoteIpAddress` in your hosting environment and configure the matching representation. Keep forwarded headers middleware before components that consume scheme, host, or client IP. This check cannot prove that a load balancer strips spoofed incoming headers or that your deployment CIDRs stay current; those need infrastructure configuration and an integration check in the deployed network.
