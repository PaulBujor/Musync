namespace SpotifyTools.Domain.Interfaces;

public interface ITrackHistoryRepository
{
    Task<List<TrackHistory>> GetAllHistoryAsync(CancellationToken ct);
    Task<List<TrackHistory>> GetActiveHistoryAsync(CancellationToken ct);
    Task AddTrackHistoryAsync(IEnumerable<TrackHistory> entries, CancellationToken ct);
    Task MarkRemovedAsync(string spotifyTrackId, string reason, DateTime removedAt, CancellationToken ct);
    Task<HashSet<string>> GetTrackIdSetAsync(CancellationToken ct);
}
