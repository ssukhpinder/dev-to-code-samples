using System.Buffers;
using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;

string pemText = string.Join('\n',
[
    "preamble",
    "-----BEGIN CONFIG-----",
    "AQIDBA==",
    "-----END CONFIG-----",
    "between blocks",
    "-----BEGIN METADATA-----",
    "aGVsbG8=",
    "-----END METADATA-----",
    "trailer",
]) + '\n';

byte[] fixture = [0xFF, (byte)'\n', .. Encoding.ASCII.GetBytes(pemText)];
IReadOnlyList<PemBlock> blocks = ScanPemBlocks(fixture);

var checks = new Verifier();
checks.Equal(2, blocks.Count, "found both PEM blocks");
checks.Equal("CONFIG", blocks[0].Label, "read the first label from UTF-8 bytes");
checks.Equal("01020304", Convert.ToHexString(blocks[0].DecodedData), "decoded the first Base64 payload");
checks.Equal("METADATA", blocks[1].Label, "read the second label from UTF-8 bytes");
checks.Equal("68656C6C6F", Convert.ToHexString(blocks[1].DecodedData), "decoded the second Base64 payload");
checks.True(blocks[0].StartOffset > 0 && blocks[1].StartOffset > blocks[0].StartOffset,
    "reported increasing absolute byte offsets");
checks.True(fixture[0] == 0xFF && blocks.Count == 2,
    "ignored a non-UTF-8 byte outside the PEM boundaries");

var strictUtf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
bool strictValidationRejectedFixture = false;
try
{
    _ = strictUtf8.GetCharCount(fixture);
}
catch (DecoderFallbackException)
{
    strictValidationRejectedFixture = true;
}

checks.True(strictValidationRejectedFixture,
    "kept whole-input UTF-8 validation as a separate policy");

byte[] mismatchedLabels = Encoding.ASCII.GetBytes(string.Join('\n',
[
    "-----BEGIN CONFIG-----",
    "AQIDBA==",
    "-----END OTHER-----",
]) + '\n');

bool mismatchedLabelsRejected = false;
try
{
    _ = PemEncoding.FindUtf8(mismatchedLabels);
}
catch (ArgumentException)
{
    mismatchedLabelsRejected = true;
}

checks.True(mismatchedLabelsRejected, "rejected mismatched BEGIN and END labels");

foreach (PemBlock block in blocks)
{
    Console.WriteLine(
        $"BLOCK {block.Label} offset={block.StartOffset} bytes={block.DecodedData.Length} hex={Convert.ToHexString(block.DecodedData)}");
}

checks.Complete();

static IReadOnlyList<PemBlock> ScanPemBlocks(ReadOnlySpan<byte> input)
{
    var blocks = new List<PemBlock>();
    ReadOnlySpan<byte> remaining = input;
    int absoluteOffset = 0;

    while (!remaining.IsEmpty)
    {
        PemFields fields;
        try
        {
            fields = PemEncoding.FindUtf8(remaining);
        }
        catch (ArgumentException)
        {
            break;
        }

        int blockStart = fields.Location.Start.GetOffset(remaining.Length);
        int blockEnd = fields.Location.End.GetOffset(remaining.Length);
        string label = Encoding.ASCII.GetString(remaining[fields.Label]);
        byte[] decodedData = new byte[fields.DecodedDataLength];
        OperationStatus decodeStatus = Base64.DecodeFromUtf8(
            remaining[fields.Base64Data],
            decodedData,
            out int bytesConsumed,
            out int bytesWritten,
            isFinalBlock: true);

        if (decodeStatus != OperationStatus.Done ||
            bytesConsumed != remaining[fields.Base64Data].Length ||
            bytesWritten != decodedData.Length)
        {
            throw new FormatException($"Could not decode the PEM payload for '{label}'.");
        }

        blocks.Add(new PemBlock(label, absoluteOffset + blockStart, decodedData));

        absoluteOffset += blockEnd;
        remaining = remaining[blockEnd..];
    }

    return blocks;
}

internal sealed record PemBlock(string Label, int StartOffset, byte[] DecodedData);

internal sealed class Verifier
{
    private int _passed;

    public void Equal<T>(T expected, T actual, string message)
        where T : IEquatable<T>
    {
        if (!actual.Equals(expected))
        {
            throw new InvalidOperationException(
                $"FAIL: {message}. Expected '{expected}', actual '{actual}'.");
        }

        Pass(message);
    }

    public void True(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException($"FAIL: {message}.");
        }

        Pass(message);
    }

    public void Complete() => Console.WriteLine($"VERIFIED {_passed}/9");

    private void Pass(string message)
    {
        _passed++;
        Console.WriteLine($"PASS {_passed:D2}: {message}");
    }
}
