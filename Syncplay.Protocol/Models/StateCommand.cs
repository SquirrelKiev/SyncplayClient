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
        [JsonPropertyName("latencyCalculation")]
        public double LatencyCalculation { get; set; }

        [JsonPropertyName("clientLatencyCalculation")]
        public double ClientLatencyCalculation { get; set; } = 0d;

        [JsonPropertyName("serverRtt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ServerRtt { get; set; }

        [JsonPropertyName("clientRtt"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public double? ClientRtt { get; set; }
    }

    [UsedImplicitly]
    public record PlayStateInfo
    {
        [JsonPropertyName("position")] public float Position { get; init; }
        [JsonPropertyName("paused")] public bool Paused { get; init; }
        [JsonPropertyName("doSeek")] public bool? DoSeek { get; init; }
        [JsonPropertyName("setBy")] public string? SetBy { get; init; }
    }

    [UsedImplicitly]
    public record IgnoringOnTheFlyInfo
    {
        [JsonPropertyName("server"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonInclude]
        private int Server { get; init; } = 0;

        [JsonPropertyName("client"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault), JsonInclude]
        private int Client { get; init; } = 0;
        
        public bool ServerIgnoring
        {
            get => Server > 0;
            init => Server = value ? 1 : 0;
        }

        public bool ClientIgnoring
        {
            get => Client > 0;
            init => Client = value ? 1 : 0;
        }
    }
}