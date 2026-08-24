const int BufferSize = 4;
byte[] payload = [1, 2, 3, 4];

using TrackingStream sink = new();
using BufferedStream buffered = new(sink, BufferSize);

foreach (byte value in payload)
{
    buffered.WriteByte(value);
}

Snapshot beforeExplicitFlush = Snapshot.Capture(sink);
buffered.Flush();
Snapshot afterExplicitFlush = Snapshot.Capture(sink);

Console.WriteLine($"Target: {TargetFramework.Name}");
Print("Before explicit Flush", beforeExplicitFlush);
Print("After explicit Flush", afterExplicitFlush);

Verifier verifier = new();

#if NET10_0
verifier.Expect(
    beforeExplicitFlush.FlushCalls == 0,
    ".NET 10 does not flush the underlying stream when the buffer becomes full");
verifier.Expect(
    beforeExplicitFlush.Bytes.SequenceEqual(payload[..^1]),
    ".NET 10 writes the first three bytes without flushing and keeps the fourth buffered");
#else
verifier.Expect(
    beforeExplicitFlush.FlushCalls == 1,
    ".NET 9 flushes the underlying stream when the fourth byte fills the buffer");
verifier.Expect(
    beforeExplicitFlush.Bytes.SequenceEqual(payload[..^1]),
    ".NET 9 exposes the first three bytes while the fourth remains buffered");
#endif

verifier.Expect(
    afterExplicitFlush.FlushCalls == beforeExplicitFlush.FlushCalls + 1,
    "an explicit Flush call reaches the underlying stream exactly once");
verifier.Expect(
    afterExplicitFlush.WriteCalls == beforeExplicitFlush.WriteCalls + 1,
    "the explicit boundary writes the bytes that remain buffered exactly once");
verifier.Expect(
    afterExplicitFlush.Bytes.SequenceEqual(payload),
    "the explicit boundary preserves byte order and content");
verifier.Expect(
    buffered.CanWrite,
    "the explicit flush leaves the buffered stream writable");
verifier.Expect(
    sink.Position == payload.Length,
    "the underlying stream position matches the payload length");

verifier.Complete();

static void Print(string label, Snapshot snapshot)
{
    string bytes = snapshot.Bytes.Length == 0
        ? "<empty>"
        : Convert.ToHexString(snapshot.Bytes);

    Console.WriteLine(
        $"{label}: flushes={snapshot.FlushCalls}, writes={snapshot.WriteCalls}, bytes={bytes}");
}

internal static class TargetFramework
{
#if NET10_0
    public const string Name = "net10.0";
#else
    public const string Name = "net9.0";
#endif
}

internal sealed class TrackingStream : MemoryStream
{
    public int FlushCalls { get; private set; }

    public int WriteCalls { get; private set; }

    public override void Flush()
    {
        FlushCalls++;
        base.Flush();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        WriteCalls++;
        base.Write(buffer, offset, count);
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        WriteCalls++;
        base.Write(buffer);
    }
}

internal readonly record struct Snapshot(int FlushCalls, int WriteCalls, byte[] Bytes)
{
    public static Snapshot Capture(TrackingStream stream) =>
        new(stream.FlushCalls, stream.WriteCalls, stream.ToArray());
}

internal sealed class Verifier
{
    private int _passed;
    private int _total;

    public void Expect(bool condition, string contract)
    {
        _total++;

        if (!condition)
        {
            Console.Error.WriteLine($"FAIL: {contract}");
            Environment.ExitCode = 1;
            return;
        }

        _passed++;
        Console.WriteLine($"PASS: {contract}");
    }

    public void Complete()
    {
        Console.WriteLine($"Verification: {_passed}/{_total} passed.");

        if (_passed != _total)
        {
            throw new InvalidOperationException("One or more BufferedStream contracts failed.");
        }
    }
}
