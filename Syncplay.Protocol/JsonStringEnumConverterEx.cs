using System.Runtime.Serialization;
using System.Text.Json;
using System.Text.Json.Serialization;

// insane that this isn't built in
// thank you https://github.com/dotnet/runtime/issues/31081#issuecomment-848697673
namespace SyncPlay.Protocol;

public class JsonStringEnumConverterEx<TEnum> : JsonConverter<TEnum> where TEnum : struct, System.Enum
{

    private readonly Dictionary<TEnum, string> enumToString = new Dictionary<TEnum, string>();
    private readonly Dictionary<string, TEnum> stringToEnum = new Dictionary<string, TEnum>();

    public JsonStringEnumConverterEx()
    {
        var type = typeof(TEnum);
        var values = System.Enum.GetValues<TEnum>();

        foreach (var value in values)
        {
            var enumMember = type.GetMember(value.ToString())[0];
            var attr = enumMember.GetCustomAttributes(typeof(EnumMemberAttribute), false)
                .Cast<EnumMemberAttribute>()
                .FirstOrDefault();

            stringToEnum.Add(value.ToString(), value);

            if (attr?.Value != null)
            {
                enumToString.Add(value, attr.Value);
                stringToEnum.Add(attr.Value, value);
            }
            else
            {
                enumToString.Add(value, value.ToString());
            }
        }
    }

    public override TEnum Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var stringValue = reader.GetString();

        return stringValue == null ? default : stringToEnum.GetValueOrDefault(stringValue);
    }

    public override void Write(Utf8JsonWriter writer, TEnum value, JsonSerializerOptions options)
    {
        writer.WriteStringValue(enumToString[value]);
    }
}