# .NET 10 LDAP `VlvRequestControl` validation

## Problem

The `VlvRequestControl(int, int, string)` constructor must encode its target as UTF-8 before an LDAP virtual-list-view request can be sent. A .NET string can still contain malformed UTF-16, such as an unpaired high or low surrogate.

`System.DirectoryServices.Protocols` 9.0.19 accepts either malformed value and replaces it with the UTF-8 bytes `EF-BF-BD`. The request can therefore carry a different comparison target from the one the caller intended. Version 10.0.11 rejects the same input immediately with `EncoderFallbackException`.

This sample multi-targets .NET 9 and .NET 10. It verifies valid ASCII and supplementary Unicode targets on both packages, proves the legacy replacement bytes are present in the BER control value, and proves the current package fails before a control can be sent.

## Prerequisites and dependencies

- .NET 10 SDK
- .NET 9 and .NET 10 runtimes
- PowerShell 5.1 or later for `verify.ps1`
- `System.DirectoryServices.Protocols` 9.0.19 for `net9.0`
- `System.DirectoryServices.Protocols` 10.0.11 for `net10.0`

No LDAP server, account, credentials, certificate, API key, database, clock, randomness, or test framework is required. There are no credential placeholders because the sample has no authenticated integration.

Restore may contact configured NuGet sources. Running the built sample makes no LDAP, HTTP, model, or other network call: it constructs controls and reads their encoded values entirely in memory.

## Setup

From this folder, restore, check formatting, and build both targets:

```powershell
dotnet restore .\LdapVlvValidation.csproj --nologo
dotnet format .\LdapVlvValidation.csproj --verify-no-changes --no-restore
dotnet build .\LdapVlvValidation.csproj --configuration Release --no-restore --nologo
```

## Run and verify

Run the deterministic cross-target verifier:

```powershell
powershell.exe -NoProfile -NonInteractive -ExecutionPolicy Bypass -File ".\verify.ps1"
```

It runs each target twice without restoring or rebuilding and ends with a fixed pass count:

```text
PASS: net9.0 verified replacement bytes for both malformed targets
PASS: net10.0 verified early EncoderFallbackException for both malformed targets
PASS: valid target encodings match across both package versions
PASS: both targets report 6/6 internal checks
PASS: repeated output is byte-for-byte deterministic
Summary: 5/5 passed
```

You can inspect either target directly:

```powershell
dotnet run --project .\LdapVlvValidation.csproj --framework net9.0 --configuration Release --no-build --no-restore
dotnet run --project .\LdapVlvValidation.csproj --framework net10.0 --configuration Release --no-build --no-restore
```

The .NET 9 run confirms that each lone surrogate becomes the replacement character bytes in both `Target` and the BER control value. The .NET 10 run confirms that each constructor call throws `EncoderFallbackException`. Both runs also confirm that `alpha` and `caf\u00E9-\U0001F600` have their exact expected UTF-8 representations.

## Limitations

This sample covers the string-target overload of `VlvRequestControl`; it does not exercise its byte-array or offset overloads, decode responses, connect to an LDAP directory, or validate filters and authentication. Early UTF-16 validation prevents replacement bytes from entering this control, but it does not prove that a valid target exists on a server.

If malformed text can enter your application before the constructor call, validate it at that boundary and handle `EncoderFallbackException` during a .NET 10 migration. Do not catch the exception and resend replacement bytes unless that lossy behavior is an explicit protocol decision.

See Microsoft's [.NET 10 LDAP compatibility note](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/10.0/ldap-directorycontrol-parsing), the [`VlvRequestControl` API reference](https://learn.microsoft.com/en-us/dotnet/api/system.directoryservices.protocols.vlvrequestcontrol?view=net-10.0), and [RFC 2891](https://www.rfc-editor.org/rfc/rfc2891) for the control's BER contract.
