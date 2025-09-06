using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record SetCommand
{
    [JsonPropertyName("user"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public Dictionary<string, SetUserInfo>? Users { get; init; }

    [JsonPropertyName("ready"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public ReadyInfo? Ready { get; init; }

    [JsonPropertyName("playlistChange"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlaylistChangeInfo? PlaylistChange { get; init; }

    [JsonPropertyName("playlistIndex"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlaylistIndexInfo? PlaylistIndex { get; init; }

    /// <remarks>Only used for client requests</remarks>
    [JsonPropertyName("file"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public MediaFile? File { get; init; }

    [UsedImplicitly] [JsonExtensionData] public Dictionary<string, JsonElement>? ExtraProperties { get; init; }

    [UsedImplicitly]
    public record SetUserInfo
    {
        [JsonPropertyName("room"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public required SetUserRoomInfo RoomInfo { get; init; }

        [UsedImplicitly]
        public class SetUserRoomInfo
        {
            [JsonPropertyName("name")] public required string Name { get; init; }
        }

        [JsonPropertyName("event"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public SetUserEventInfo? EventInfo { get; init; }

        [UsedImplicitly]
        public record SetUserEventInfo
        {
            [JsonPropertyName("joined"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Joined { get; init; }

            [JsonPropertyName("left"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
            public bool Left { get; init; }

            [JsonPropertyName("version"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public string? Version { get; init; }

            [JsonPropertyName("features"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public FeatureSet? Features { get; init; }
        }

        [JsonPropertyName("file"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public MediaFile? FileInfo { get; init; }
    }

    [UsedImplicitly]
    public record ReadyInfo
    {
        [JsonPropertyName("username")] public string? Username { get; init; }
        [JsonPropertyName("isReady")] public bool? IsReady { get; init; }

        [JsonPropertyName("manuallyInitiated")]
        public bool ManuallyInitiated { get; init; }
    }

    [UsedImplicitly]
    public record PlaylistChangeInfo
    {
        [JsonPropertyName("user"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ChangedBy { get; init; }

        [JsonPropertyName("files")] public required List<string> Files { get; init; }
    }

    [UsedImplicitly]
    public record PlaylistIndexInfo
    {
        [JsonPropertyName("user"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? ChangedBy { get; init; }

        [JsonPropertyName("index")] public int? Index { get; init; }
    }
}