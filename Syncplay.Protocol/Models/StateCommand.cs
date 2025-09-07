using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public record StateCommand
{
    [JsonPropertyName("ping"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PingInfo? Ping { get; set; }

    [JsonPropertyName("playstate"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public PlayStateInfo? PlayState { get; set; }

    [JsonPropertyName("ignoringOnTheFly"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public IgnoringOnTheFlyInfo? IgnoringOnTheFly { get; set; }

    [UsedImplicitly]
    public record PingInfo
    {
        // not sure what type this should be
        [JsonPropertyName("latencyCalculation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? LatencyCalculation { get; init; }

        [JsonPropertyName("clientLatencyCalculation"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ClientLatencyCalculation { get; init; }

        [JsonPropertyName("serverRtt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ServerRtt { get; init; }

        [JsonPropertyName("clientRtt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ClientRtt { get; init; }
    }

    [UsedImplicitly]
    public record PlayStateInfo
    {
        [JsonPropertyName("position")] public float Position { get; init; }
        [JsonPropertyName("paused")] public bool Paused { get; init; }

        [JsonPropertyName("doSeek"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public bool? DoSeek { get; init; }

        [JsonPropertyName("setBy"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string? SetBy { get; init; }
    }

    [UsedImplicitly]
    public record IgnoringOnTheFlyInfo
    {
        [JsonPropertyName("server"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonInclude]
        private int Server { get; init; } = 0;

        [JsonPropertyName("client"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonInclude]
        private int Client { get; init; } = 0;

        [JsonIgnore]
        public bool ServerIgnoring
        {
            get => Server > 0;
            init => Server = value ? 1 : 0;
        }

        [JsonIgnore]
        public bool ClientIgnoring
        {
            get => Client > 0;
            init => Client = value ? 1 : 0;
        }
    }
}