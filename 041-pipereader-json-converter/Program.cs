using System.IO.Pipelines;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

const int PipeBufferSize = 4_096;
const int SourceChunkSize = 257;

var expectedCode = $"acct-{new string('x', 12_000)}-end";
var payload = JsonSerializer.SerializeToUtf8Bytes(new { code = expectedCode });
var verifier = new Verifier();

var streamConverter = new BrokenCustomerCodeConverter();
var streamResult = await DeserializeFromStreamAsync(payload, streamConverter);
verifier.Expect(
    !streamConverter.SawValueSequence && streamResult.Code.Value == expectedCode,
    "stream keeps the converter token contiguous");

var brokenPipeConverter = new BrokenCustomerCodeConverter();
var brokenPipeResult = await DeserializeFromChunkedPipeAsync(payload, brokenPipeConverter);
verifier.Expect(
    brokenPipeConverter.SawValueSequence,
    "PipeReader exposes a segmented JSON token");
verifier.Expect(
    brokenPipeResult.Code.Value != expectedCode,
    "ValueSpan-only converter loses the segmented token");

var fixedPipeConverter = new SequenceAwareCustomerCodeConverter();
var fixedPipeResult = await DeserializeFromChunkedPipeAsync(payload, fixedPipeConverter);
verifier.Expect(
    fixedPipeConverter.SawValueSequence && fixedPipeResult.Code.Value == expectedCode,
    "sequence-aware converter preserves the segmented token");

var escapedPayload = Encoding.UTF8.GetBytes("{\"code\":\"line\\nbreak\"}");
var escapedConverter = new SequenceAwareCustomerCodeConverter();
var escapedResult = await DeserializeFromStreamAsync(escapedPayload, escapedConverter);
verifier.Expect(
    escapedResult.Code.Value == "line\nbreak",
    "GetString decodes JSON escapes");

Console.WriteLine(
    $"Observed: payload={payload.Length} bytes, pipe-buffer={PipeBufferSize}, " +
    $"source-chunk={SourceChunkSize}, broken-value={brokenPipeResult.Code.Value.Length} chars");

verifier.Finish();

static JsonSerializerOptions CreateOptions(JsonConverter<CustomerCode> converter)
{
    var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
    {
        DefaultBufferSize = 32_768
    };

    options.Converters.Add(converter);
    return options;
}

static async Task<Envelope> DeserializeFromStreamAsync(
    byte[] payload,
    JsonConverter<CustomerCode> converter)
{
    await using var stream = new MemoryStream(payload, writable: false);
    return await JsonSerializer.DeserializeAsync<Envelope>(stream, CreateOptions(converter))
        ?? throw new JsonException("The stream payload produced no envelope.");
}

static async Task<Envelope> DeserializeFromChunkedPipeAsync(
    byte[] payload,
    JsonConverter<CustomerCode> converter)
{
    using var stream = new ChunkedReadStream(payload, SourceChunkSize);
    var reader = PipeReader.Create(
        stream,
        new StreamPipeReaderOptions(
            bufferSize: PipeBufferSize,
            minimumReadSize: SourceChunkSize,
            leaveOpen: false));

    try
    {
        return await JsonSerializer.DeserializeAsync<Envelope>(reader, CreateOptions(converter))
            ?? throw new JsonException("The pipe payload produced no envelope.");
    }
    finally
    {
        await reader.CompleteAsync();
    }
}

internal sealed record Envelope(CustomerCode Code);

internal readonly record struct CustomerCode(string Value);

internal sealed class BrokenCustomerCodeConverter : JsonConverter<CustomerCode>
{
    public bool SawValueSequence { get; private set; }

    public override CustomerCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("CustomerCode must be a JSON string.");
        }

        SawValueSequence |= reader.HasValueSequence;

        // This worked while the token always arrived in one contiguous buffer.
        // ValueSpan is empty when HasValueSequence is true.
        return new CustomerCode(Encoding.UTF8.GetString(reader.ValueSpan));
    }

    public override void Write(
        Utf8JsonWriter writer,
        CustomerCode value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class SequenceAwareCustomerCodeConverter : JsonConverter<CustomerCode>
{
    public bool SawValueSequence { get; private set; }

    public override CustomerCode Read(
        ref Utf8JsonReader reader,
        Type typeToConvert,
        JsonSerializerOptions options)
    {
        if (reader.TokenType is not JsonTokenType.String)
        {
            throw new JsonException("CustomerCode must be a JSON string.");
        }

        SawValueSequence |= reader.HasValueSequence;

        // GetString handles ValueSpan, ValueSequence, and JSON escaping.
        return new CustomerCode(
            reader.GetString()
            ?? throw new JsonException("CustomerCode cannot be null."));
    }

    public override void Write(
        Utf8JsonWriter writer,
        CustomerCode value,
        JsonSerializerOptions options) =>
        writer.WriteStringValue(value.Value);
}

internal sealed class ChunkedReadStream(byte[] payload, int maximumChunkSize) : Stream
{
    private int _offset;

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => payload.Length;

    public override long Position
    {
        get => _offset;
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) =>
        Read(buffer.AsSpan(offset, count));

    public override int Read(Span<byte> buffer)
    {
        if (_offset >= payload.Length)
        {
            return 0;
        }

        var count = Math.Min(
            Math.Min(buffer.Length, maximumChunkSize),
            payload.Length - _offset);

        payload.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;
        return count;
    }

    public override ValueTask<int> ReadAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin) =>
        throw new NotSupportedException();

    public override void SetLength(long value) =>
        throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) =>
        throw new NotSupportedException();
}

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

    public void Finish()
    {
        Console.WriteLine($"Verifier: {_passed}/{_total} passed");
        Environment.ExitCode = _passed == _total ? 0 : 1;
    }
}
