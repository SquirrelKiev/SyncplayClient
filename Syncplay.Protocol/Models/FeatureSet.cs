using System.Text.Json.Serialization;

namespace SyncPlay.Protocol.Models;

public record FeatureSet
{
    [JsonPropertyName("sharedPlaylists")]
    public bool SharedPlaylists { get; init; } = false;

    [JsonPropertyName("chat")]
    public bool Chat { get; init; } = true;

    [JsonPropertyName("featureList")]
    public bool FeatureList { get; init; } = true;

    [JsonPropertyName("readiness")]
    public bool Readiness { get; init; } = false;

    [JsonPropertyName("managedRooms")]
    public bool ManagedRooms { get; init; } = false;

    [JsonPropertyName("persistentRooms")]
    public bool PersistentRooms { get; init; } = false;

    [JsonPropertyName("uiMode")]
    public string UiMode { get; init; } = "CLI";
}