using System.Text.Json;
using System.Text.Json.Serialization;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

public record RootCommand
{
    public RootCommand()
    {
    }

    public RootCommand(TlsSupportCommand tls)
    {
        Tls = tls;
    }

    public RootCommand(HelloCommand hello)
    {
        Hello = hello;
    }

    public RootCommand(SetCommand set)
    {
        Set = set;
    }

    public RootCommand(StateCommand state)
    {
        State = state;
    }

    public RootCommand(ErrorCommand error)
    {
        Error = error;
    }

    public RootCommand(ChatCommand chat)
    {
        Chat = chat;
    }

    [JsonPropertyName("TLS"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public TlsSupportCommand? Tls { get; init; }

    [JsonPropertyName("Hello"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public HelloCommand? Hello { get; init; }

    [JsonPropertyName("Set"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public SetCommand? Set { get; init; }

    [JsonPropertyName("State"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public StateCommand? State { get; init; }

    [JsonPropertyName("Error"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public ErrorCommand? Error { get; init; }
    
    [JsonPropertyName("Chat"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public ChatCommand? Chat { get; init; }
    
    /// <remarks> First layer of dictionary has the key as the room name, second layer is the key as the username. </remarks>
    [JsonPropertyName("List"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull), UsedImplicitly]
    public Dictionary<string, Dictionary<string, ListCommandUserInfo>>? UserList { get; init; }

    [UsedImplicitly] [JsonExtensionData] public Dictionary<string, JsonElement> ExtraProperties { get; init; } = [];

    public string ToJson()
    {
        return JsonSerializer.Serialize(this);
    }
}