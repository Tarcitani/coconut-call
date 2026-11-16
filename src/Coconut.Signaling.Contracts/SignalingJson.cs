using System.Text.Json;
using System.Text.Json.Serialization;

namespace Coconut.Signaling.Contracts;

/// <summary>Serialização única do protocolo, usada pelo servidor e pelos clientes.</summary>
public static class SignalingJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ServerMessage message) => JsonSerializer.Serialize(message, Options);

    public static string Serialize(ClientMessage message) => JsonSerializer.Serialize(message, Options);

    public static ClientMessage? DeserializeClient(ReadOnlySpan<byte> utf8) => JsonSerializer.Deserialize<ClientMessage>(utf8, Options);

    public static ServerMessage? DeserializeServer(string json) => JsonSerializer.Deserialize<ServerMessage>(json, Options);

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            // Mensagens maliciosas profundas são rejeitadas cedo.
            MaxDepth = 16,
            AllowOutOfOrderMetadataProperties = true,
            RespectNullableAnnotations = true,
        };
        options.Converters.Add(new GuidIdConverter<ParticipantId>(g => new ParticipantId(g), id => id.Value));
        options.Converters.Add(new GuidIdConverter<ShareSessionId>(g => new ShareSessionId(g), id => id.Value));
        options.Converters.Add(new GuidIdConverter<ShareRequestId>(g => new ShareRequestId(g), id => id.Value));
        options.Converters.Add(new JsonStringEnumConverter());
        options.MakeReadOnly(populateMissingResolver: true);
        return options;
    }

    private sealed class GuidIdConverter<T>(Func<Guid, T> create, Func<T, Guid> value) : JsonConverter<T>
        where T : struct
    {
        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
            reader.TokenType == JsonTokenType.String && reader.TryGetGuid(out var guid)
                ? create(guid)
                : throw new JsonException($"{typeof(T).Name} inválido");

        public override void Write(Utf8JsonWriter writer, T id, JsonSerializerOptions options) =>
            writer.WriteStringValue(value(id)); // formato "D", o único aceito por Utf8JsonReader.TryGetGuid
    }
}
