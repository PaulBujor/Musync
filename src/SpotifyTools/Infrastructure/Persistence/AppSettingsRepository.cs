using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class AppSettingsRepository : IAppSettingsRepository
{
    private readonly SpotifyDbContext _db;

    public AppSettingsRepository(SpotifyDbContext db)
    {
        _db = db;
    }

    public async Task<string?> GetAsync(string key, CancellationToken ct)
    {
        return (await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct))?.Value;
    }

    public async Task SetAsync(string key, string value, CancellationToken ct)
    {
        var existing = await _db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (existing is null)
        {
            _db.AppSettings.Add(new AppSetting
            {
                Key = key,
                Value = value,
                UpdatedAt = DateTime.UtcNow
            });
        }
        else
        {
            existing.Value = value;
            existing.UpdatedAt = DateTime.UtcNow;
        }

        await _db.SaveChangesAsync(ct);
    }
}
