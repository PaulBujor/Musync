namespace SpotifyTools.Domain.Interfaces;

public interface IAuthenticator
{
    Task EnsureAuthenticatedAsync(CancellationToken ct);
}
