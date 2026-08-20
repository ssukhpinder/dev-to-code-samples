using System.Security.Cryptography;

var verifier = new Verifier();

// Public RFC 5649 test values. Never use these bytes as production keys.
var keyEncryptionKey = Convert.FromHexString(
    "5840DF6E29B02AF1AB493B705BF16EA1AE8338F4DCC176A8");
var twentyByteKey = Convert.FromHexString(
    "C37B7E6492584340BED12207808941155068F738");
var expectedTwentyByteWrap = Convert.FromHexString(
    "138BDEAA9B8FA7FC61F97742E72248EE5AE6AE5360D1AE6A5F54F373FA543B6A");
var sevenByteKey = Convert.FromHexString("466F7250617369");
var expectedSevenByteWrap = Convert.FromHexString(
    "AFBEB0F07DFBF5419200F2CCB50BB24F");

using var aes = Aes.Create();
aes.SetKey(keyEncryptionKey);

verifier.Expect(
    Aes.GetKeyWrapPaddedLength(twentyByteKey.Length) == expectedTwentyByteWrap.Length,
    "20-byte key needs a 32-byte wrapped buffer");

var wrappedTwentyByteKey = aes.EncryptKeyWrapPadded(twentyByteKey);
verifier.Expect(
    wrappedTwentyByteKey.SequenceEqual(expectedTwentyByteWrap),
    "20-byte RFC 5649 vector matches");

var unwrappedTwentyByteKey = aes.DecryptKeyWrapPadded(wrappedTwentyByteKey);
verifier.Expect(
    unwrappedTwentyByteKey.SequenceEqual(twentyByteKey),
    "20-byte key unwraps exactly");

verifier.Expect(
    Aes.GetKeyWrapPaddedLength(sevenByteKey.Length) == expectedSevenByteWrap.Length,
    "7-byte key needs a 16-byte wrapped buffer");

Span<byte> wrappedSevenByteKey =
    stackalloc byte[Aes.GetKeyWrapPaddedLength(sevenByteKey.Length)];
aes.EncryptKeyWrapPadded(sevenByteKey, wrappedSevenByteKey);
verifier.Expect(
    wrappedSevenByteKey.SequenceEqual(expectedSevenByteWrap),
    "7-byte RFC 5649 vector matches");

Span<byte> unwrappedSevenByteKey = stackalloc byte[sevenByteKey.Length];
var bytesWritten = aes.DecryptKeyWrapPadded(
    wrappedSevenByteKey,
    unwrappedSevenByteKey);
verifier.Expect(
    bytesWritten == sevenByteKey.Length &&
    unwrappedSevenByteKey.SequenceEqual(sevenByteKey),
    "span overload returns the original 7-byte key");

using var wrongAes = Aes.Create();
wrongAes.SetKey(new byte[keyEncryptionKey.Length]);
verifier.ExpectThrows<CryptographicException>(
    () => wrongAes.DecryptKeyWrapPadded(wrappedTwentyByteKey),
    "wrong key-encryption key is rejected");

var tamperedWrap = wrappedTwentyByteKey.ToArray();
tamperedWrap[^1] ^= 0x01;
verifier.ExpectThrows<CryptographicException>(
    () => aes.DecryptKeyWrapPadded(tamperedWrap),
    "one-bit wrapped-key change is rejected");

Console.WriteLine(
    $"Observed: 20 -> {wrappedTwentyByteKey.Length} bytes, " +
    $"7 -> {wrappedSevenByteKey.Length} bytes");

CryptographicOperations.ZeroMemory(keyEncryptionKey);
CryptographicOperations.ZeroMemory(twentyByteKey);
CryptographicOperations.ZeroMemory(sevenByteKey);
CryptographicOperations.ZeroMemory(unwrappedTwentyByteKey);
CryptographicOperations.ZeroMemory(wrappedTwentyByteKey);
CryptographicOperations.ZeroMemory(tamperedWrap);

verifier.Finish();

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
