namespace SpotifyTools.Domain.Interfaces;

public interface IJobRunRepository
{
    Task CreateAsync(JobRun jobRun, CancellationToken ct);
    Task UpdateAsync(JobRun jobRun, CancellationToken ct);
    Task<JobRun?> GetLatestAsync(CancellationToken ct);
}