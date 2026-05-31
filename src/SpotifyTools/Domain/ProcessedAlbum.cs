namespace SpotifyTools.Domain;

public sealed class ProcessedAlbum
{
    public Guid Id { get; set; }
    public string SpotifyAlbumId { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public DateTime FirstProcessedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}
