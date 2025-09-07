namespace SyncPlay.Protocol.Models;

// should I allow seeking and forced state changes in this too?
public record PassiveStateReport(float Position, bool Paused);