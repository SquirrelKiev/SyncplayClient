using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record ErrorCommand
{
    [JsonPropertyName("message")]
    public required string Message { get; init; }
}