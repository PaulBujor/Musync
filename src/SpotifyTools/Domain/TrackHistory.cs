namespace SpotifyTools.Domain;

public sealed class TrackHistory
{
    public Guid Id { get; set; }
    public Guid JobRunId { get; set; }
    public string SpotifyTrackId { get; set; } = "";
    public string TrackName { get; set; } = "";
    public string ArtistName { get; set; } = "";
    public string AlbumName { get; set; } = "";
    public DateTime AddedAt { get; set; }
    public DateTime? RemovedAt { get; set; }
    public string? RemovalReason { get; set; }
}
