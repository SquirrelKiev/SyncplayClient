using System.Diagnostics;

namespace SyncPlay.Protocol.Models;

public class SyncplayUser
{
    internal SyncplayUser()
    {
    }

    public SyncplayUser(string username, SetCommand.SetUserInfo userData)
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
    }

    public SyncplayUser(string username, ListCommandUserInfo userInfo)
    {
        UpdateProperties(userInfo);

        Username = username;
    }

    public void UpdateProperties(ListCommandUserInfo userInfo)
    {
        IsReady = userInfo.IsReady ?? false;
        IsController = userInfo.Controller;

        Features = userInfo.Features;
        Position = userInfo.Position;
        FileInfo = userInfo.FileInfo;
    }

    public string Username { get; internal set; } = null!;
    public bool IsReady { get; internal set; }
    public bool IsPaused { get; internal set; }
    public float Position { get; internal set; }
    public MediaFile? FileInfo { get; internal set; }
    public bool IsController { get; internal set; }

    public string? Version { get; internal set; }
    public FeatureSet Features { get; internal set; } = null!;
}