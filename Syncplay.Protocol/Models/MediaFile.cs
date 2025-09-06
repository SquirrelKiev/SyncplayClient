using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record MediaFile
{
    [JsonPropertyName("name")] public required string Name { get; init; }

    [JsonPropertyName("duration")] public required float Duration { get; init; }

    // FileSize can either be a number (the actual file size or just 0 if the room member isn't sharing) or the number, but hashed.
    // The hash is sent as a json string, the number is sent as... a json number. 
    // C# is a statically typed language.
    // Curse you Python.
    [JsonPropertyName("size")] public required StringLongUnion FileSize { get; init; }

    public MediaFile()
    {
    }

    [SetsRequiredMembers]
    public MediaFile(string name, float duration, long fileSize)
    {
        Name = name;
        Duration = duration;
        FileSize = fileSize;
    }
}

[JsonConverter(typeof(StringLongUnionConverter))]
public readonly record struct StringLongUnion
{
    public string? StringValue { get; }
    public long? LongValue { get; }

    [MemberNotNullWhen(true, nameof(StringValue))]
    public bool IsString => StringValue is not null;

    [MemberNotNullWhen(true, nameof(LongValue))]
    public bool IsLong => LongValue.HasValue;

    public StringLongUnion(string value)
    {
        StringValue = value;
        LongValue = null;
    }

    public StringLongUnion(long value)
    {
        LongValue = value;
        StringValue = null;
    }

    public static implicit operator StringLongUnion(string value) => new(value);
    public static implicit operator StringLongUnion(long value) => new(value);

    public override string ToString() => IsString ? StringValue : LongValue?.ToString() ?? "";
}

public class StringLongUnionConverter : JsonConverter<StringLongUnion>
{
    public override StringLongUnion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String)
        {
            string str = reader.GetString()!;
            return new StringLongUnion(str);
        }

        if (reader.TokenType == JsonTokenType.Number)
        {
            var num = reader.GetInt64();
            return new StringLongUnion(num);
        }

        throw new JsonException($"Expected string or number token, got {reader.TokenType}");
    }

    public override void Write(Utf8JsonWriter writer, StringLongUnion value, JsonSerializerOptions options)
    {
        if (value.IsString)
        {
            writer.WriteStringValue(value.StringValue);
        }
        else if (value.IsLong)
        {
            writer.WriteNumberValue(value.LongValue.Value);
        }
        else
        {
            writer.WriteNullValue();
        }
    }
}