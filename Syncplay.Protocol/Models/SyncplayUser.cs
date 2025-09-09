using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using JetBrains.Annotations;

namespace SyncPlay.Protocol.Models;

public class RoomUser
{
    // so JSON deserialization can create an instance of this class
    [UsedImplicitly]
#pragma warning disable CS8618
    internal RoomUser()
    {
    }
#pragma warning restore CS8618

    public RoomUser(string username, SetCommand.SetUserInfo userData)
    {
        Debug.Assert(userData.EventInfo != null);
        Debug.Assert(userData.EventInfo.Version != null);
        Debug.Assert(userData.EventInfo.Features != null);

        IsReady = false;
        IsPaused = true;
        Position = 0;
        FileInfo = userData.FileInfo;
        Version = userData.EventInfo.Version;
        Features = userData.EventInfo.Features;

        Username = username;
        RoomName = userData.RoomInfo.Name;
    }

    public RoomUser(string username, ListCommandUserInfo userInfo, string roomName)
    {
        UpdateProperties(userInfo, roomName);

        Username = username;
    }

    [MemberNotNull(nameof(Features), nameof(RoomName))]
    public void UpdateProperties(ListCommandUserInfo userInfo, string roomName)
    {
        IsReady = userInfo.IsReady ?? false;
        IsController = userInfo.Controller;

        Features = userInfo.Features;
        Position = userInfo.Position;
        FileInfo = userInfo.FileInfo;
        RoomName = roomName;
    }

    public string RoomName { get; internal set; }
    public string Username { get; internal set; } = null!;
    public bool IsReady { get; internal set; }
    public bool IsPaused { get; internal set; }
    public float Position { get; internal set; }
    public MediaFile? FileInfo { get; internal set; }
    public bool IsController { get; internal set; }

    public string? Version { get; internal set; }
    public FeatureSet Features { get; internal set; }
}