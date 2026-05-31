namespace SpotifyTools.Domain.Interfaces;

public interface ITidalAuthenticator
{
    Task EnsureAuthenticatedAsync(CancellationToken ct);
}
