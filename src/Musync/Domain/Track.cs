namespace Musync.Domain;

public sealed record Track(string Id, string Name, string Artist, string Album, string? Isrc = null);