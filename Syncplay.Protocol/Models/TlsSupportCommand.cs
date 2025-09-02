using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record TlsSupportCommand
{
    [JsonConverter(typeof(JsonStringEnumConverterEx<TlsState>))]
    public enum TlsState
    {
        [EnumMember(Value = "send")]
        Send,
        [EnumMember(Value = "true")]
        True,
        [EnumMember(Value = "false")]
        False
    }

    [JsonPropertyName("startTLS")]
    public required TlsState StartTls { get; set; }
}