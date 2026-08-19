using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

var socket = new ScriptedWebSocket(
    Frame.Text("{\"Id\":", endOfMessage: false),
    Frame.Text("42,\"Text\":\"", endOfMessage: false),
    Frame.Text("ready\"}", endOfMessage: true),
    Frame.Text("{\"Id\":43,\"Text\":\"next\"}", endOfMessage: true));

AppMessage first = await ReceiveJsonMessageAsync(socket, CancellationToken.None);
Assert(first == new AppMessage(42, "ready"), "The fragmented first message was reconstructed.");
Assert(socket.ReceiveCalls == 3, "The first read stopped at its WebSocket message boundary.");

AppMessage second = await ReceiveJsonMessageAsync(socket, CancellationToken.None);
Assert(second == new AppMessage(43, "next"), "The second message remained available for the next read.");
Assert(socket.ReceiveCalls == 4, "Exactly four scripted frames were consumed.");

Console.WriteLine("PASS: fragmented JSON deserialized as one message");
Console.WriteLine("PASS: the next WebSocket message was not consumed early");
Console.WriteLine($"PASS: receive calls = {socket.ReceiveCalls}");

static async Task<AppMessage> ReceiveJsonMessageAsync(
    WebSocket webSocket,
    CancellationToken cancellationToken)
{
    // Disposing before end-of-message aborts the socket, so malformed JSON makes
    // this receive fail closed instead of leaving an unread message tail behind.
    using Stream messageStream = WebSocketStream.CreateReadableMessageStream(webSocket);

    return await JsonSerializer.DeserializeAsync<AppMessage>(
        messageStream,
        cancellationToken: cancellationToken)
        ?? throw new JsonException("The WebSocket message contained JSON null.");
}

static void Assert(bool condition, string message)
{
    if (!condition)
    {
        throw new InvalidOperationException($"FAILED: {message}");
    }
}

internal sealed record AppMessage(int Id, string Text);

internal sealed record Frame(byte[] Payload, WebSocketMessageType MessageType, bool EndOfMessage)
{
    public static Frame Text(string value, bool endOfMessage) =>
        new(Encoding.UTF8.GetBytes(value), WebSocketMessageType.Text, endOfMessage);
}

internal sealed class ScriptedWebSocket(params Frame[] frames) : WebSocket
{
    private readonly Queue<Frame> _frames = new(frames);
    private Frame? _currentFrame;
    private int _currentOffset;
    private WebSocketState _state = WebSocketState.Open;

    public int ReceiveCalls { get; private set; }

    public override WebSocketCloseStatus? CloseStatus => null;

    public override string? CloseStatusDescription => null;

    public override WebSocketState State => _state;

    public override string? SubProtocol => null;

    public override void Abort() => _state = WebSocketState.Aborted;

    public override Task CloseAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        _state = WebSocketState.Closed;
        return Task.CompletedTask;
    }

    public override Task CloseOutputAsync(
        WebSocketCloseStatus closeStatus,
        string? statusDescription,
        CancellationToken cancellationToken)
    {
        _state = WebSocketState.CloseSent;
        return Task.CompletedTask;
    }

    public override void Dispose() => _state = WebSocketState.Closed;

    public override Task<WebSocketReceiveResult> ReceiveAsync(
        ArraySegment<byte> buffer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceiveChunk(buffer.AsSpan(), out int count, out WebSocketMessageType messageType, out bool endOfMessage);

        return Task.FromResult(new WebSocketReceiveResult(count, messageType, endOfMessage));
    }

    public override ValueTask<ValueWebSocketReceiveResult> ReceiveAsync(
        Memory<byte> buffer,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ReceiveChunk(buffer.Span, out int count, out WebSocketMessageType messageType, out bool endOfMessage);

        return ValueTask.FromResult(new ValueWebSocketReceiveResult(count, messageType, endOfMessage));
    }

    public override Task SendAsync(
        ArraySegment<byte> buffer,
        WebSocketMessageType messageType,
        bool endOfMessage,
        CancellationToken cancellationToken) =>
        throw new NotSupportedException("This deterministic fixture only exercises receive behavior.");

    private void ReceiveChunk(
        Span<byte> destination,
        out int count,
        out WebSocketMessageType messageType,
        out bool endOfMessage)
    {
        if (_currentFrame is null)
        {
            if (!_frames.TryDequeue(out _currentFrame))
            {
                throw new InvalidOperationException("The verifier requested an unscripted WebSocket frame.");
            }

            _currentOffset = 0;
        }

        ReceiveCalls++;

        int remaining = _currentFrame.Payload.Length - _currentOffset;
        count = Math.Min(destination.Length, remaining);
        _currentFrame.Payload.AsSpan(_currentOffset, count).CopyTo(destination);
        _currentOffset += count;

        messageType = _currentFrame.MessageType;
        bool frameConsumed = _currentOffset == _currentFrame.Payload.Length;
        endOfMessage = frameConsumed && _currentFrame.EndOfMessage;

        if (frameConsumed)
        {
            _currentFrame = null;
            _currentOffset = 0;
        }
    }
}
