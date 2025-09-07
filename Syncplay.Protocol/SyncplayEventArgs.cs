using SyncPlay.Protocol.Models;

namespace SyncPlay.Protocol;

public record PlaylistChangedEventArgs(
    IReadOnlyList<string> OldPlaylist,
    IReadOnlyList<string> Playlist,
    SyncplayUser? ChangedBy);

public record PlaylistIndexChangedEventArgs(int OldIndex, int Index, SyncplayUser? ChangedBy);

public record UserFileChangedEventArgs(SyncplayUser User, MediaFile? PreviousFile);

public record UserReadyStateChangedEventArgs(SyncplayUser User, SyncplayUser? ChangedBy);