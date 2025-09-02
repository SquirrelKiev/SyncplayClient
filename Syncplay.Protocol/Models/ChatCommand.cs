using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record ChatCommand
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
    
    [JsonPropertyName("username")]
    public required string Username { get; init; }
}