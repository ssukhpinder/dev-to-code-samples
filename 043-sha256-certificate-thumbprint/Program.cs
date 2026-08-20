using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

var verifier = new Verifier();

using var rsa = RSA.Create(2048);
var request = new CertificateRequest(
    new X500DistinguishedName("CN=thumbprint-lookup-demo"),
    rsa,
    HashAlgorithmName.SHA256,
    RSASignaturePadding.Pkcs1);

using var generated = request.CreateSelfSigned(
    new DateTimeOffset(2020, 1, 1, 0, 0, 0, TimeSpan.Zero),
    new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero));
using var certificate = X509CertificateLoader.LoadCertificate(
    generated.Export(X509ContentType.Cert));

var certificates = new X509Certificate2Collection(certificate);
var sha1Thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA1);
var sha256Thumbprint = certificate.GetCertHash(HashAlgorithmName.SHA256);
var sha1Hex = Convert.ToHexString(sha1Thumbprint);
var sha256Hex = Convert.ToHexString(sha256Thumbprint);

verifier.Expect(
    sha1Thumbprint.Length == 20 && sha256Thumbprint.Length == 32,
    "SHA-1 and SHA-256 thumbprints have distinct expected lengths");

var bytesMatch = certificates.FindByThumbprint(
    HashAlgorithmName.SHA256,
    sha256Thumbprint);
verifier.Expect(
    IsSingleMatch(bytesMatch, certificate),
    "SHA-256 byte thumbprint finds the certificate");

var lowercaseHexMatch = certificates.FindByThumbprint(
    HashAlgorithmName.SHA256,
    sha256Hex.ToLowerInvariant());
verifier.Expect(
    IsSingleMatch(lowercaseHexMatch, certificate),
    "SHA-256 hexadecimal lookup is case-insensitive");

var changedThumbprint = sha256Thumbprint.ToArray();
changedThumbprint[^1] ^= 0x01;
verifier.Expect(
    certificates.FindByThumbprint(
        HashAlgorithmName.SHA256,
        changedThumbprint).Count == 0,
    "changed SHA-256 thumbprint does not match");

var legacySha256Match = certificates.Find(
    X509FindType.FindByThumbprint,
    sha256Hex,
    validOnly: false);
verifier.Expect(
    legacySha256Match.Count == 0,
    "legacy FindByThumbprint does not treat SHA-256 as SHA-1");

var legacySha1Match = certificates.Find(
    X509FindType.FindByThumbprint,
    sha1Hex,
    validOnly: false);
verifier.Expect(
    IsSingleMatch(legacySha1Match, certificate),
    "legacy FindByThumbprint still searches the SHA-1 thumbprint");

verifier.ExpectThrows<ArgumentException>(
    () => certificates.FindByThumbprint(
        HashAlgorithmName.SHA256,
        "not-a-hex-thumbprint"),
    "malformed hexadecimal thumbprint is rejected");

var verificationTime = new DateTimeOffset(
    2030,
    1,
    1,
    0,
    0,
    0,
    TimeSpan.Zero);

verifier.Expect(
    IsSingleMatch(bytesMatch, certificate) &&
    certificate.NotAfter.ToUniversalTime() < verificationTime.UtcDateTime,
    "thumbprint lookup identifies a certificate without validating its lifetime");

Console.WriteLine(
    $"Observed: SHA-1={sha1Thumbprint.Length} bytes, " +
    $"SHA-256={sha256Thumbprint.Length} bytes");

verifier.Finish();

static bool IsSingleMatch(
    X509Certificate2Collection matches,
    X509Certificate2 expected) =>
    matches.Count == 1 &&
    matches[0].RawDataMemory.Span.SequenceEqual(expected.RawDataMemory.Span);

internal sealed class Verifier
{
    private int _passed;
    private int _total;

    public void Expect(bool condition, string name)
    {
        _total++;

        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS {name}");
            return;
        }

        Console.Error.WriteLine($"FAIL {name}");
    }

    public void ExpectThrows<TException>(Action action, string name)
        where TException : Exception
    {
        try
        {
            action();
            Expect(false, name);
        }
        catch (TException)
        {
            Expect(true, name);
        }
    }

    public void Finish()
    {
        Console.WriteLine($"Verifier: {_passed}/{_total} passed");
        Environment.ExitCode = _passed == _total ? 0 : 1;
    }
}
