# .NET 10 AES KeyWrap with Padding

## Problem

Applications sometimes need to store a content-encryption or MAC key outside the key-management system that protects the key-encryption key (KEK). Encrypting that key material with an improvised AES mode creates an interoperability and integrity-checking problem, especially when the key is not a multiple of eight bytes.

.NET 10 adds `Aes.EncryptKeyWrapPadded` and `Aes.DecryptKeyWrapPadded` for the RFC 5649 AES Key Wrap with Padding algorithm. This sample verifies both official RFC vectors, including 20-byte and 7-byte inputs, then proves that a wrong KEK and a one-bit change to the wrapped value are rejected.

## Prerequisites

- .NET 10 SDK
- No credentials, packages, database, network service, or paid API

The hexadecimal values in `Program.cs` are public RFC test vectors, not secrets. In a real application, load the KEK through a key-management or secret-storage boundary such as `<load-kek-from-approved-key-store>`; do not place it in source code, logs, or configuration committed to Git.

## Restore and run

From the repository root:

```powershell
dotnet restore .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj
dotnet run --project .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj -c Release --no-restore
```

On Bash-compatible shells, replace the backslashes with `/`.

Expected behavior:

```text
PASS 20-byte key needs a 32-byte wrapped buffer
PASS 20-byte RFC 5649 vector matches
PASS 20-byte key unwraps exactly
PASS 7-byte key needs a 16-byte wrapped buffer
PASS 7-byte RFC 5649 vector matches
PASS span overload returns the original 7-byte key
PASS wrong key-encryption key is rejected
PASS one-bit wrapped-key change is rejected
Observed: 20 -> 32 bytes, 7 -> 16 bytes
Verifier: 8/8 passed
```

The verifier exits with code `1` if any contract fails.

## Format, build, and dependency checks

```powershell
dotnet format .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj --verify-no-changes --no-restore
dotnet build .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj -c Release --no-restore
dotnet list .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj package --include-transitive
dotnet list .\042-aes-key-wrap-padding\AesKeyWrapPadding.csproj package --vulnerable --include-transitive
```

The project targets `net10.0` and uses only the installed .NET runtime libraries. After restore, verification is offline and deterministic.

## Production boundaries

AES-KWP is for cryptographic key material, not bulk application data. The KEK must be 128, 192, or 256 bits and at least as strong as the key it protects. Keep KEKs in a key-management boundary, attach a version or key identifier to the stored envelope, define rotation and rewrapping procedures, and clear temporary key buffers when practical.

Successful unwrap provides the RFC 5649 integrity check; it does not identify who created the wrapped value or enforce authorization. If an attacker can replace an entire wrapped value with another valid one, bind the envelope to its owner and purpose in authenticated metadata or enforce that binding in the key-management layer.

Primary references:

- [.NET 10 cryptography additions](https://learn.microsoft.com/en-us/dotnet/core/whats-new/dotnet-10/libraries#aes-keywrap-with-padding-ietf-rfc-5649)
- [`Aes.EncryptKeyWrapPadded` API](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.aes.encryptkeywrappadded?view=net-10.0)
- [RFC 5649: AES Key Wrap with Padding](https://www.rfc-editor.org/rfc/rfc5649.html)
