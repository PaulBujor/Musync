using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Tests.Infrastructure;

public sealed class ActiveTrackHistoryIndexTests
{
    private static AppDbContext BuildDb()
    {
        var services = new ServiceCollection();
        services.AddDbContext<AppDbContext>(options => options.UseSqlite("Data Source=:memory:"));
        var db = services.BuildServiceProvider().GetRequiredService<AppDbContext>();
        db.Database.OpenConnection();
        db.Database.EnsureCreated();
        return db;
    }

    private static TrackHistory Active(string trackId) => new()
    {
        Id = Guid.CreateVersion7(),
        JobRunId = Guid.CreateVersion7(),
        Provider = "spotify",
        TrackId = trackId,
        AddedAt = DateTime.UtcNow
    };

    [Fact]
    public async Task TwoActiveRowsForSameTrack_Rejected()
    {
        var db = BuildDb();
        db.TrackHistories.Add(Active("track-1"));
        await db.SaveChangesAsync();

        db.TrackHistories.Add(Active("track-1"));

        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task RemovedRowPlusActiveRow_Allowed()
    {
        var db = BuildDb();
        var removed = Active("track-1");
        removed.RemovedAt = DateTime.UtcNow;
        db.TrackHistories.Add(removed);
        db.TrackHistories.Add(Active("track-1"));

        await db.SaveChangesAsync();

        Assert.Equal(2, await db.TrackHistories.CountAsync());
        Assert.Equal(1, await db.TrackHistories.CountAsync(x => x.RemovedAt == null));
    }
}
