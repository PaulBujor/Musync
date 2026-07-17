namespace Musync.Domain.Interfaces;

public interface IAuthenticator
{
    Task EnsureAuthenticatedAsync(CancellationToken ct);
}