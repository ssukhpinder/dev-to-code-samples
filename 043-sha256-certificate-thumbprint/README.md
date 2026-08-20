# .NET 10 SHA-256 Certificate Thumbprint Lookup

## Problem

`X509Certificate2Collection.Find(X509FindType.FindByThumbprint, ...)` searches the SHA-1 thumbprint. Passing a SHA-256 thumbprint to that legacy API therefore returns no match, even when the certificate is present.

.NET 10 adds `FindByThumbprint(HashAlgorithmName, ...)`, which makes the digest algorithm explicit. This sample creates a self-signed public test certificate in memory, calculates both thumbprints, and proves the behavior of the legacy and new lookups. It never opens or changes an operating-system certificate store.

## Prerequisites and setup

- .NET 10 SDK
- No credentials, packages, certificate-store access, database, network service, or paid API

There is no configuration step. The certificate and its temporary private key are generated only in process and disposed before exit. In a real application, load a certificate through an approved boundary such as `<load-certificate-from-configured-store>`; never commit a production certificate's private key or password.

## Restore and run

From the repository root:

```powershell
dotnet restore .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj
dotnet run --project .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj -c Release --no-restore
```

On Bash-compatible shells, replace the backslashes with `/`.

Expected behavior:

```text
PASS SHA-1 and SHA-256 thumbprints have distinct expected lengths
PASS SHA-256 byte thumbprint finds the certificate
PASS SHA-256 hexadecimal lookup is case-insensitive
PASS changed SHA-256 thumbprint does not match
PASS legacy FindByThumbprint does not treat SHA-256 as SHA-1
PASS legacy FindByThumbprint still searches the SHA-1 thumbprint
PASS malformed hexadecimal thumbprint is rejected
PASS thumbprint lookup identifies a certificate without validating its lifetime
Observed: SHA-1=20 bytes, SHA-256=32 bytes
Verifier: 8/8 passed
```

The verifier exits with code `1` if any contract fails.

## Format, build, and dependency checks

```powershell
dotnet format .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj --verify-no-changes --no-restore
dotnet build .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj -c Release --no-restore
dotnet list .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj package --include-transitive
dotnet list .\043-sha256-certificate-thumbprint\CertificateThumbprintLookup.csproj package --vulnerable --include-transitive
```

The project targets `net10.0` and uses only the installed runtime libraries. Restore and the vulnerability audit can contact configured package sources; the verifier itself is offline and deterministic.

## Production boundaries

A thumbprint identifies certificate bytes; it does not establish trust. After lookup, validate the certificate chain, allowed root, lifetime, intended purpose, revocation policy, and application-specific identity. The sample uses a fixed comparison time of 2030 to demonstrate that lookup still returns a certificate outside its validity period.

Store SHA-256 thumbprints with an explicit algorithm identifier instead of inferring an algorithm from text length. Normalize user-provided hexadecimal only through an API that validates it, and decide whether duplicate collection matches are an error rather than silently taking the first result.

Primary references:

- [.NET 10 certificate thumbprint additions](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#find-certificates-by-thumbprints-other-than-sha-1)
- [`X509Certificate2Collection.FindByThumbprint` API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate2collection.findbythumbprint?view=net-10.0)
