using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol;

[UsedImplicitly]
public record MediaFile
{
    [JsonPropertyName("duration")]
    public float Duration { get; set; }

    [JsonPropertyName("name")] public string FileName { get; set; } = null!;
    // FileSize can either be a number (the actual file size or just 0 if the room member isn't sharing) or the number, but hashed.
    // The hash is sent as a json string, the number is sent as... a json number. 
    // C# is a statically typed language.
    // Curse you Python.
    [JsonPropertyName("size")]
    public StringFloatUnion FileSize { get; set; }
}

[JsonConverter(typeof(StringFloatUnionConverter))]
public readonly record struct StringFloatUnion
{
    public string? StringValue { get; }
    public float? FloatValue { get; }

    [MemberNotNullWhen(true, nameof(StringValue))]
    public bool IsString => StringValue is not null;
    
    [MemberNotNullWhen(true, nameof(FloatValue))]
    public bool IsFloat => FloatValue.HasValue;

    public StringFloatUnion(string value)
    {
        StringValue = value;
        FloatValue = null;
    }

    public StringFloatUnion(float value)
    {
        FloatValue = value;
        StringValue = null;
    }

    public override string ToString() => IsString ? StringValue : FloatValue?.ToString() ?? "";
}

public class StringFloatUnionConverter : JsonConverter<StringFloatUnion>
{
    public override StringFloatUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString()!;
            return new StringFloatUnion(str);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            float num = reader.GetSingle();
            return new StringFloatUnion(num);
        }

        throw new JsonException($"Expected string or number token, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, StringFloatUnion value, JsonSerializerOptions options)
    {
        if (value.IsString)
        {
            writer.WriteStringValue(value.StringValue);
        }
        else if (value.IsFloat)
        {
            writer.WriteNumberValue(value.FloatValue.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}