namespace SpotifyTools.Domain.Interfaces;

public interface IAppSettingsRepository
{
    Task<string?> GetAsync(string key, CancellationToken ct);
    Task SetAsync(string key, string value, CancellationToken ct);
}