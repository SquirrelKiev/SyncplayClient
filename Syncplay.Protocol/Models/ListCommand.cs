using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record ListCommandUserInfo
{
    [JsonPropertyName("position"), UsedImplicitly]
    public required int Position { get; init; }

    [JsonPropertyName("file"), JsonConverter(typeof(JsonNullAsEmptyObjectConverter<MediaFile>)), UsedImplicitly]
    public required MediaFile? FileInfo { get; init; } // the server can return this as {} for some reason

    [JsonPropertyName("controller"), UsedImplicitly]
    public required bool Controller { get; init; }

    [JsonPropertyName("isReady"), UsedImplicitly]
    public required bool? IsReady { get; init; }

    [JsonPropertyName("features"), UsedImplicitly]
    public required FeatureSet Features { get; init; }
}

public class JsonNullAsEmptyObjectConverter<T> : JsonConverter<T> where T : class
{
    private static readonly JsonConverter<T> DefaultConverter =
        (JsonConverter<T>)JsonSerializerOptions.Default.GetConverter(typeof(T));

    public override T? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Null:
                return null;
            case JsonTokenType.StartObject:
            {
                using var document = JsonDocument.ParseValue(ref reader);

                // is the object empty?
                if (!document.RootElement.EnumerateObject().Any())
                {
                    return null;
                }

                // have to do this to avoid recursive calls to this converter
                var newOptions = new JsonSerializerOptions(options);
                for (int i = newOptions.Converters.Count - 1; i >= 0; i--)
                {
                    if (newOptions.Converters[i] is JsonNullAsEmptyObjectConverter<T>)
                    {
                        newOptions.Converters.RemoveAt(i);
                    }
                }

                return document.RootElement.Deserialize<T>(newOptions);
            }
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when parsing a {typeToConvert}.");
        }
    }

    public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
    {
        DefaultConverter.Write(writer, value, options);
    }
}