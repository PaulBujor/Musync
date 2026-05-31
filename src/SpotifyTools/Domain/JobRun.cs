namespace SpotifyTools.Domain;

public sealed class JobRun
{
    public Guid Id { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string Status { get; set; } = "succeeded";
    public int TracksAdded { get; set; }
    public int TracksRemovedLiked { get; set; }
    public int TracksRemovedManual { get; set; }
    public int TracksSkipped { get; set; }
    public int NewAlbumsEncountered { get; set; }
    public int QueueSizeAfter { get; set; }
    public string? ErrorMessage { get; set; }
}
