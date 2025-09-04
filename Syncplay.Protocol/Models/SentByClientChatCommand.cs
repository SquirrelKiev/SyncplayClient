using System.Text.Json.Serialization;

namespace SyncPlay.Protocol.Models;

public record SentByClientChatCommand
{
    [JsonPropertyName("Chat")] public required string Message { get; init; }
}