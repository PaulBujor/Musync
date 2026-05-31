namespace SpotifyTools.Domain.Interfaces;

public interface ISpotifyAuthenticator
{
    Task EnsureAuthenticatedAsync(CancellationToken ct);
}
