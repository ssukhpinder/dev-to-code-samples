using System.Text.Json;

namespace Nu1510.MultiTargetFixture;

public static class JsonEnvelope
{
    public static string Serialize(string message, int count) =>
        JsonSerializer.Serialize(new Payload(message, count));

    private sealed class Payload
    {
        public Payload(string message, int count)
        {
            Message = message;
            Count = count;
        }

        public string Message { get; }

        public int Count { get; }
    }
}
