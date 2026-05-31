using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Infrastructure.Persistence;

namespace SpotifyTools.Jobs;

public sealed class JobOrchestrator
{
    private readonly SpotifyDbContext _db;
    private readonly SyncStep1_SnapshotAndDiff _step1;
    private readonly SyncStep2_AddNewTracks _step2;
    private readonly SyncStep3_GenerateReport _step3;
    private readonly ILogger<JobOrchestrator> _logger;

    public JobOrchestrator(
        SpotifyDbContext db,
        SyncStep1_SnapshotAndDiff step1,
        SyncStep2_AddNewTracks step2,
        SyncStep3_GenerateReport step3,
        ILogger<JobOrchestrator> logger)
    {
        _db = db;
        _step1 = step1;
        _step2 = step2;
        _step3 = step3;
        _logger = logger;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        var jobRun = new JobRun
        {
            Id = Guid.CreateVersion7(),
            StartedAt = DateTime.UtcNow,
            Status = "running"
        };

        _db.JobRuns.Add(jobRun);
        await _db.SaveChangesAsync(ct);

        using var _ = _logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.StartingJob(_logger, jobRun.Id.ToString());

        try
        {
            await _step1.ExecuteAsync(jobRun, ct);
            await _step2.ExecuteAsync(jobRun, ct);

            jobRun.Status = "succeeded";
            jobRun.FinishedAt = DateTime.UtcNow;
            _db.JobRuns.Update(jobRun);
            await _db.SaveChangesAsync(ct);

            await _step3.ExecuteAsync(jobRun, ct);
        }
        catch (OperationCanceledException)
        {
            jobRun.Status = "partial";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = "Cancelled by user";
            _db.JobRuns.Update(jobRun);
            await _db.SaveChangesAsync(ct);
            await _step3.ExecuteAsync(jobRun, ct);
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(_logger, ex.Message, ex);

            jobRun.Status = "failed";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = ex.Message;
            _db.JobRuns.Update(jobRun);
            await _db.SaveChangesAsync(ct);

            await _step3.ExecuteAsync(jobRun, ct);
            throw;
        }
    }
}
