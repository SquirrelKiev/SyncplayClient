using SyncPlay.Protocol.Models;

namespace SyncPlay.Protocol;

public record PlaylistChangedEventArgs(
    IReadOnlyList<string> OldPlaylist,
    IReadOnlyList<string> Playlist,
    string? ChangedBy);

public record PlaylistIndexChangedEventArgs(int OldIndex, int Index, string? ChangedBy);

public record UserFileChangedEventArgs(SyncplayUser User, MediaFile? PreviousFile);