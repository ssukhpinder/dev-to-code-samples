using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

const string PfxPasswordPlaceholder = "<PFX_PASSWORD>";
const string WrongPasswordPlaceholder = "<WRONG_PFX_PASSWORD>";

const string Pbes2Oid = "1.2.840.113549.1.5.13";
const string Aes256CbcOid = "2.16.840.1.101.3.4.1.42";
const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
const string Pkcs12TripleDesSha1Oid = "1.2.840.113549.1.12.1.3";
const string Sha1Oid = "1.3.14.3.2.26";

using var rsa = RSA.Create(2048);
var request = new CertificateRequest(
    "CN=pkcs12-export-policy-demo",
    rsa,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);

using var certificate = request.CreateSelfSigned(
    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2040, 1, 1, 0, 0, 0, TimeSpan.Zero));

var modernPfx = ExportForReader(
    certificate,
    PfxReaderProfile.Modern,
    PfxPasswordPlaceholder);
var legacyPfx = ExportForReader(
    certificate,
    PfxReaderProfile.Legacy,
    PfxPasswordPlaceholder);

var checks = new[]
{
    new Verification(
        "modern profile uses PBES2",
        ContainsOid(modernPfx, Pbes2Oid)),
    new Verification(
        "modern profile uses AES-256-CBC",
        ContainsOid(modernPfx, Aes256CbcOid)),
    new Verification(
        "modern profile uses SHA-256",
        ContainsOid(modernPfx, Sha256Oid)),
    new Verification(
        "modern profile excludes legacy PBE and SHA-1 identifiers",
        !ContainsOid(modernPfx, Pkcs12TripleDesSha1Oid) &&
        !ContainsOid(modernPfx, Sha1Oid)),
    new Verification(
        "legacy profile uses PKCS#12 3DES/SHA-1 PBE",
        ContainsOid(legacyPfx, Pkcs12TripleDesSha1Oid)),
    new Verification(
        "legacy profile uses SHA-1",
        ContainsOid(legacyPfx, Sha1Oid)),
    new Verification(
        "legacy profile excludes PBES2, AES-256, and SHA-256 identifiers",
        !ContainsOid(legacyPfx, Pbes2Oid) &&
        !ContainsOid(legacyPfx, Aes256CbcOid) &&
        !ContainsOid(legacyPfx, Sha256Oid)),
    new Verification(
        "both profiles re-import the certificate and private key",
        RoundTrips(modernPfx, certificate, PfxPasswordPlaceholder) &&
        RoundTrips(legacyPfx, certificate, PfxPasswordPlaceholder)),
    new Verification(
        "both profiles reject the wrong password",
        RejectsWrongPassword(modernPfx, WrongPasswordPlaceholder) &&
        RejectsWrongPassword(legacyPfx, WrongPasswordPlaceholder)),
};

var passed = 0;
foreach (var check in checks)
{
    Console.WriteLine($"{(check.Passed ? "PASS" : "FAIL")} {check.Name}");
    passed += check.Passed ? 1 : 0;
}

Console.WriteLine($"Verifier: {passed}/{checks.Length} passed");
return passed == checks.Length ? 0 : 1;

static byte[] ExportForReader(
    X509Certificate2 certificate,
    PfxReaderProfile profile,
    string password)
{
    var parameters = profile switch
    {
        PfxReaderProfile.Modern =>
            Pkcs12ExportPbeParameters.Pbes2Aes256Sha256,
        PfxReaderProfile.Legacy =>
            Pkcs12ExportPbeParameters.Pkcs12TripleDesSha1,
        _ => throw new ArgumentOutOfRangeException(nameof(profile)),
    };

    return certificate.ExportPkcs12(parameters, password);
}

static bool ContainsOid(ReadOnlySpan<byte> encoded, string oid)
{
    // This is a controlled-fixture regression check, not a structural PFX parser.
    var writer = new AsnWriter(AsnEncodingRules.DER);
    writer.WriteObjectIdentifier(oid);
    return encoded.IndexOf(writer.Encode()) >= 0;
}

static bool RoundTrips(
    ReadOnlySpan<byte> pfx,
    X509Certificate2 expected,
    string password)
{
    using var loaded = X509CertificateLoader.LoadPkcs12(
        pfx,
        password,
        X509KeyStorageFlags.EphemeralKeySet);

    return loaded.HasPrivateKey &&
        loaded.RawDataMemory.Span.SequenceEqual(expected.RawDataMemory.Span);
}

static bool RejectsWrongPassword(ReadOnlySpan<byte> pfx, string wrongPassword)
{
    try
    {
        using var unexpected = X509CertificateLoader.LoadPkcs12(
            pfx,
            wrongPassword,
            X509KeyStorageFlags.EphemeralKeySet);
        return false;
    }
    catch (CryptographicException)
    {
        return true;
    }
}

internal enum PfxReaderProfile
{
    Modern,
    Legacy,
}

internal sealed record Verification(string Name, bool Passed);
