using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class TrackHistoryRepository : ITrackHistoryRepository
{
    private readonly SpotifyDbContext _db;

    public TrackHistoryRepository(SpotifyDbContext db)
    {
        _db = db;
    }

    public async Task<List<TrackHistory>> GetAllHistoryAsync(CancellationToken ct)
    {
        return await _db.TrackHistories.OrderBy(x => x.AddedAt).ToListAsync(ct);
    }

    public async Task<List<TrackHistory>> GetActiveHistoryAsync(CancellationToken ct)
    {
        return await _db.TrackHistories.Where(x => x.RemovedAt == null).ToListAsync(ct);
    }

    public async Task AddTrackHistoryAsync(IEnumerable<TrackHistory> entries, CancellationToken ct)
    {
        _db.TrackHistories.AddRange(entries);
        await _db.SaveChangesAsync(ct);
    }

    public async Task MarkRemovedAsync(string spotifyTrackId, string reason, DateTime removedAt, CancellationToken ct)
    {
        var entries = await _db.TrackHistories
            .Where(x => x.SpotifyTrackId == spotifyTrackId && x.RemovedAt == null)
            .ToListAsync(ct);

        foreach (var entry in entries)
        {
            entry.RemovedAt = removedAt;
            entry.RemovalReason = reason;
        }

        await _db.SaveChangesAsync(ct);
    }

    public async Task<HashSet<string>> GetTrackIdSetAsync(CancellationToken ct)
    {
        var ids = await _db.TrackHistories.Select(x => x.SpotifyTrackId).Distinct().ToListAsync(ct);
        return new HashSet<string>(ids);
    }
}
