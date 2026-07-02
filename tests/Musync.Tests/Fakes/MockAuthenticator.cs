using Musync.Domain.Interfaces;
using Musync.Infrastructure.Persistence;

namespace Musync.Tests.Fakes;

public sealed class MockAuthenticator(AppDbContext db) : IAuthenticator
{
    public int CallCount { get; private set; }

    public async Task EnsureAuthenticatedAsync(CancellationToken ct)
    {
        CallCount++;
        db.RefreshTokens.Add(new Musync.Domain.RefreshToken
        {
            Id = Guid.CreateVersion7(),
            Token = "fresh-refresh-token",
            Provider = "test",
            UpdatedAt = DateTime.UtcNow
        });
        await db.SaveChangesAsync(ct);
    }
}
