using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Domain.Interfaces;

namespace SpotifyTools.Jobs;

public sealed class JobOrchestrator
{
    private readonly IJobRunRepository _jobRunRepo;
    private readonly SyncStep1_SnapshotAndDiff _step1;
    private readonly SyncStep2_AddNewTracks _step2;
    private readonly SyncStep4_GenerateReport _step4;
    private readonly ILogger<JobOrchestrator> _logger;

    public JobOrchestrator(
        IJobRunRepository jobRunRepo,
        SyncStep1_SnapshotAndDiff step1,
        SyncStep2_AddNewTracks step2,
        SyncStep4_GenerateReport step4,
        ILogger<JobOrchestrator> logger)
    {
        _jobRunRepo = jobRunRepo;
        _step1 = step1;
        _step2 = step2;
        _step4 = step4;
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

        await _jobRunRepo.CreateAsync(jobRun, ct);

        using var _ = _logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.StartingJob(_logger, jobRun.Id.ToString());

        try
        {
            await _step1.ExecuteAsync(jobRun, ct);
            await _step2.ExecuteAsync(jobRun, ct);

            jobRun.Status = "succeeded";
            jobRun.FinishedAt = DateTime.UtcNow;
            await _jobRunRepo.UpdateAsync(jobRun, ct);

            await _step4.ExecuteAsync(jobRun, ct);
        }
        catch (OperationCanceledException)
        {
            jobRun.Status = "partial";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = "Cancelled by user";
            await _jobRunRepo.UpdateAsync(jobRun, ct);
            await _step4.ExecuteAsync(jobRun, ct);
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(_logger, ex.Message, ex);

            jobRun.Status = "failed";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = ex.Message;
            await _jobRunRepo.UpdateAsync(jobRun, ct);

            await _step4.ExecuteAsync(jobRun, ct);
            throw;
        }
    }
}
