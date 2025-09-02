using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

[UsedImplicitly]
public class HelloCommand
{
    public const string SimulatedVersion = "1.7.4";
    
    [JsonPropertyName("username")]
    public required string Username { get; init; }

    /// <summary>
    /// MD5 hash of the password.
    /// </summary>
    [JsonPropertyName("password"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? PasswordHash { get; init; }

    [JsonPropertyName("room")]
    public required RoomInfo RoomInfo { get; init; }

    [JsonPropertyName("version")]
    public string Version { get; init; } = "1.2.255";

    [JsonPropertyName("realversion")]
    public string RealVersion { get; init; } = SimulatedVersion;

    [JsonPropertyName("features")]
    public FeatureSet Features { get; init; } = new FeatureSet();

    /// <remarks>
    /// Sent from server-side. Don't include in client hellos.
    /// </remarks>
    [JsonPropertyName("motd"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? MessageOfTheDay { get; init; } = null;
}

public class RoomInfo
{
    [JsonPropertyName("name")]
    public required string Name { get; init; }
}