using System.Buffers;

ReadOnlySpan<byte> lowercaseWireId = "4bf92f3577b34da6a3ce929d0e0e4736"u8;
ReadOnlySpan<byte> uppercaseWireId = "4BF92F3577B34DA6A3CE929D0E0E4736"u8;
ReadOnlySpan<byte> expectedBinary =
[
    0x4b, 0xf9, 0x2f, 0x35, 0x77, 0xb3, 0x4d, 0xa6,
    0xa3, 0xce, 0x92, 0x9d, 0x0e, 0x0e, 0x47, 0x36,
];

var verifier = new Verifier();

Span<byte> decoded = stackalloc byte[TraceIdCodec.BinaryLength];
OperationStatus status = TraceIdCodec.Decode(lowercaseWireId, decoded, out int consumed, out int written);

verifier.Check(status == OperationStatus.Done, "valid lowercase trace ID decoded");
verifier.Check(
    consumed == TraceIdCodec.EncodedLength && written == TraceIdCodec.BinaryLength,
    "decoder reported exact consumption and output");
verifier.Check(decoded.SequenceEqual(expectedBinary), "decoded bytes matched the fixture");

Span<byte> encoded = stackalloc byte[TraceIdCodec.EncodedLength];
bool encodedSuccessfully = Convert.TryToHexStringLower(decoded, encoded, out int encodedBytes);

verifier.Check(
    encodedSuccessfully && encodedBytes == TraceIdCodec.EncodedLength,
    "lowercase encoder filled the bounded destination");
verifier.Check(encoded.SequenceEqual(lowercaseWireId), "lowercase round trip matched the wire ID");

Span<byte> uppercaseDecoded = stackalloc byte[TraceIdCodec.BinaryLength];
status = TraceIdCodec.Decode(uppercaseWireId, uppercaseDecoded, out _, out _);
verifier.Check(
    status == OperationStatus.Done && uppercaseDecoded.SequenceEqual(expectedBinary),
    "uppercase input decoded to the same bytes");

Span<byte> scratch = stackalloc byte[TraceIdCodec.BinaryLength];
status = Convert.FromHexString("abc"u8, scratch, out _, out _);
verifier.Check(status == OperationStatus.NeedMoreData, "odd-length input returned NeedMoreData");

status = TraceIdCodec.Decode(
    "4bf92f3577b34da6a3ce929d0e0e473g"u8,
    scratch,
    out _,
    out _);
verifier.Check(status == OperationStatus.InvalidData, "non-hex input returned InvalidData");

Span<byte> smallBinaryDestination = stackalloc byte[TraceIdCodec.BinaryLength - 1];
status = TraceIdCodec.Decode(lowercaseWireId, smallBinaryDestination, out _, out _);
verifier.Check(
    status == OperationStatus.DestinationTooSmall,
    "undersized decode destination was rejected");

Span<byte> smallHexDestination = stackalloc byte[TraceIdCodec.EncodedLength - 1];
verifier.Check(
    !Convert.TryToHexStringLower(decoded, smallHexDestination, out _),
    "undersized encode destination was rejected");

status = TraceIdCodec.Decode("4bf92f3577b34da6"u8, scratch, out _, out _);
verifier.Check(status == OperationStatus.InvalidData, "protocol length guard rejected a short ID");

verifier.Complete();

internal static class TraceIdCodec
{
    public const int BinaryLength = 16;
    public const int EncodedLength = BinaryLength * 2;

    public static OperationStatus Decode(
        ReadOnlySpan<byte> utf8Source,
        Span<byte> destination,
        out int bytesConsumed,
        out int bytesWritten)
    {
        bytesConsumed = 0;
        bytesWritten = 0;

        if (utf8Source.Length != EncodedLength)
        {
            return OperationStatus.InvalidData;
        }

        if (destination.Length < BinaryLength)
        {
            return OperationStatus.DestinationTooSmall;
        }

        return Convert.FromHexString(
            utf8Source,
            destination[..BinaryLength],
            out bytesConsumed,
            out bytesWritten);
    }
}

internal sealed class Verifier
{
    private int _passed;
    private int _failed;

    public void Check(bool condition, string name)
    {
        if (condition)
        {
            _passed++;
            Console.WriteLine($"PASS {name}");
            return;
        }

        _failed++;
        Console.WriteLine($"FAIL {name}");
    }

    public void Complete()
    {
        Console.WriteLine($"{_passed}/{_passed + _failed} checks passed.");

        if (_failed > 0)
        {
            Environment.ExitCode = 1;
        }
    }
}
