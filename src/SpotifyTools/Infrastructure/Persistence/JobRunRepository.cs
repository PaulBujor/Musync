using Microsoft.EntityFrameworkCore;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Infrastructure.Persistence;

public sealed class JobRunRepository : IJobRunRepository
{
    private readonly SpotifyDbContext _db;

    public JobRunRepository(SpotifyDbContext db)
    {
        _db = db;
    }

    public async Task CreateAsync(JobRun jobRun, CancellationToken ct)
    {
        _db.JobRuns.Add(jobRun);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(JobRun jobRun, CancellationToken ct)
    {
        _db.JobRuns.Update(jobRun);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<JobRun?> GetLatestAsync(CancellationToken ct)
    {
        return await _db.JobRuns.OrderByDescending(x => x.StartedAt).FirstOrDefaultAsync(ct);
    }
}
