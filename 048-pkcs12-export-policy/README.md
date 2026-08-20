# .NET 10 PKCS#12 Export Policy

## Problem

An exported PKCS#12/PFX file has two competing requirements: use modern protection for current readers, or preserve compatibility with readers that only understand older password-based encryption. A call that doesn't name the algorithm leaves that decision hidden.

.NET 10 adds `X509Certificate.ExportPkcs12`, which accepts an explicit protection profile. This sample makes AES-256/SHA-256 the normal choice and keeps Triple-DES/SHA-1 behind a deliberately named legacy path.

## What the sample demonstrates

The console app creates a self-signed certificate and private key in memory, then exports it twice:

- `Pbes2Aes256Sha256` for current readers; and
- `Pkcs12TripleDesSha1` only for a documented legacy-reader requirement.

Against this controlled fixture, the verifier finds the expected DER-encoded algorithm identifiers, rejects identifiers from the opposite profile, re-imports both artifacts with their private key, and proves that a wrong password is rejected. It never writes a PFX, certificate, or private key to disk.

## Prerequisites and setup

- .NET 10 SDK
- No package dependency, certificate-store access, credential, external service, database, or paid API

There is no setup step. The literal `<PFX_PASSWORD>` is an obvious local placeholder used only against in-memory test data. In production, obtain a real PFX password from an approved secret provider; never place it in source control, logs, command history, or the article sample.

## Restore and run

From the repository root:

```powershell
dotnet restore .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj
dotnet run --project .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj -c Release --no-restore
```

On Bash-compatible shells, replace the backslashes with `/`.

Expected behavior:

```text
PASS modern profile uses PBES2
PASS modern profile uses AES-256-CBC
PASS modern profile uses SHA-256
PASS modern profile excludes legacy PBE and SHA-1 identifiers
PASS legacy profile uses PKCS#12 3DES/SHA-1 PBE
PASS legacy profile uses SHA-1
PASS legacy profile excludes PBES2, AES-256, and SHA-256 identifiers
PASS both profiles re-import the certificate and private key
PASS both profiles reject the wrong password
Verifier: 9/9 passed
```

The process exits with code `1` if any contract fails.

## Format, build, and dependency checks

```powershell
dotnet format .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj --verify-no-changes --no-restore
dotnet build .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj -c Release --no-restore
dotnet list .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj package --include-transitive
dotnet list .\048-pkcs12-export-policy\Pkcs12ExportPolicy.csproj package --vulnerable --include-transitive
```

The project targets `net10.0` and uses only installed runtime libraries. Restore and the vulnerability audit can contact configured package sources; the verifier itself performs no network call and prints the same pass/fail contract on every successful run.

## Production boundaries

AES-256/SHA-256 protects the exported container; it doesn't validate certificate trust, choose a safe storage location, rotate the password, or control who can read the private key after import. Treat the PFX and its password as separate secrets and validate the certificate chain, purpose, identity, and lifetime at the consumption boundary.

Some old readers don't understand PBES2 with AES-256/SHA-256. Use the legacy profile only after identifying and testing that reader, document the exception, and plan its removal. Platform cryptographic policy can also disable legacy algorithms, so compatibility isn't guaranteed merely because the enum exists.

The sample's `ContainsOid` helper searches for a DER-encoded OID byte sequence in a controlled artifact. It is useful as a narrow regression assertion here, but it isn't a structural PKCS#12 parser and must not be used to classify arbitrary production PFX files. An OID can also appear in an embedded certificate or other data.

Primary references:

- [.NET 10 PKCS#12/PFX export algorithms](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#encryption-algorithm-for-pkcs12pfx-export)
- [`X509Certificate.ExportPkcs12`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.x509certificate.exportpkcs12?view=net-10.0)
- [`Pkcs12ExportPbeParameters`](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.x509certificates.pkcs12exportpbeparameters?view=net-10.0)
