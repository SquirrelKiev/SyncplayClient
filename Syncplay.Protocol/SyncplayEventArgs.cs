using SyncPlay.Protocol.Models;

namespace SyncPlay.Protocol;

public record PlaylistChangedEventArgs(
    IReadOnlyList<string> OldPlaylist,
    IReadOnlyList<string> Playlist,
    RoomUser? ChangedBy);

public record PlaylistIndexChangedEventArgs(int OldIndex, int Index, RoomUser? ChangedBy);

public record UserFileChangedEventArgs(RoomUser User, MediaFile? PreviousFile);

public record UserReadyStateChangedEventArgs(RoomUser User, RoomUser? ChangedBy);

public record UserRoomChangedEventArgs(RoomUser User, string OldRoomName);