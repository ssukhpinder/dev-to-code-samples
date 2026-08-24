using System.Text.Json;

var payload = new DemoPayload("framework-provided", 10);
Console.WriteLine(JsonSerializer.Serialize(payload));

internal sealed record DemoPayload(string Message, int Count);
