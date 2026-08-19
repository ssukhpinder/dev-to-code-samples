# .NET 10 WebSocketStream JSON message boundaries

This sample shows how to deserialize exactly one JSON WebSocket message with
`.NET 10` and `WebSocketStream.CreateReadableMessageStream`.

## Problem

A WebSocket message can arrive through several `ReceiveAsync` calls. At the
same time, a connection can carry many messages. Buffering until the socket
closes is too broad, while treating one receive call as one JSON document is
too narrow.

The message-oriented `WebSocketStream` ends after the next complete WebSocket
message. That gives `JsonSerializer.DeserializeAsync` the end-of-stream signal
it needs without consuming the following message.

## Prerequisites

- .NET 10 SDK
- No credentials, network service, or paid API

The project has no external package dependencies.

## Setup

From this folder, restore the project:

```powershell
dotnet restore WebSocketJsonMessage.csproj --nologo
```

## Run and verify

Format-check and build the sample:

```powershell
dotnet format WebSocketJsonMessage.csproj --verify-no-changes --no-restore --verbosity minimal
dotnet build WebSocketJsonMessage.csproj --configuration Release --no-restore --nologo
```

Run the deterministic verifier:

```powershell
dotnet run --project WebSocketJsonMessage.csproj --configuration Release --no-build
```

Expected output:

```text
PASS: fragmented JSON deserialized as one message
PASS: the next WebSocket message was not consumed early
PASS: receive calls = 4
```

The fixture supplies two JSON messages entirely in memory. The first is split
across three scripted `ReceiveAsync` results; the second occupies one result.
Assertions prove that the first deserialization consumes three chunks and leaves
the second message for the next call.

## Use the pattern

The application method is deliberately small:

```csharp
using Stream messageStream =
    WebSocketStream.CreateReadableMessageStream(webSocket);

AppMessage? message = await JsonSerializer.DeserializeAsync<AppMessage>(
    messageStream,
    cancellationToken: cancellationToken);
```

Create a new readable message stream for every message. Use
`WebSocketStream.Create` instead when the protocol is intentionally continuous
and has its own framing, such as a line-oriented text protocol.

## Limitations

The fake socket verifies fragmentation and message boundaries without opening
a network connection. It does not exercise a WebSocket handshake, proxy
behavior, backpressure, close messages, or application-level size limits.

`CreateReadableMessageStream` does not expose whether the message was Text or
Binary. A protocol that must reject one type should retain or wrap the raw
`ReceiveAsync` loop. A production path should also cap accepted JSON size and
apply cancellation or timeouts.

Disposing the readable message stream before it reaches end-of-message aborts
the underlying socket. That can happen when deserialization rejects malformed
JSON. Either accept that fail-closed behavior or drain the remainder safely
before disposal when the protocol permits recovery.
