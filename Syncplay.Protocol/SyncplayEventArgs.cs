namespace SyncPlay.Protocol;

public record PlaylistChangedEventArgs(IReadOnlyList<string> OldPlaylist, IReadOnlyList<string> Playlist, string? ChangedBy);
public record PlaylistIndexChangedEventArgs(int OldIndex, int Index, string? ChangedBy);