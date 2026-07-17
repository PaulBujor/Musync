using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs.Sync;

public sealed class QueueAlbumsOrchestrator(
    AppDbContext db,
    SnapshotAndDiff step1,
    AddNewTracks step2,
    GenerateReport step3,
    ILogger<QueueAlbumsOrchestrator> logger)
{
    public async Task RunAsync(SyncRunContext ctx, CancellationToken ct)
    {
        var jobRun = new JobRun
        {
            Id = Guid.CreateVersion7(),
            StartedAt = DateTime.UtcNow,
            Status = JobStatus.Running,
            ProviderName = ctx.ProviderName,
            Command = "queue-albums",
            DryRun = ctx.DryRun,
            Limit = ctx.Limit
        };

        if (!ctx.DryRun)
        {
            db.JobRuns.Add(jobRun);
            await db.SaveChangesAsync(ct);
        }

        using var _ = logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.StartingJob(logger, "queue-albums", ctx.ProviderName, jobRun.Id.ToString());

        async Task FinalizeAsync(string status, CancellationToken token, string? errorMessage = null)
        {
            jobRun.Status = status;
            jobRun.FinishedAt = DateTime.UtcNow;
            if (errorMessage is not null)
                jobRun.ErrorMessage = errorMessage;

            if (!ctx.DryRun)
                await db.SaveChangesAsync(token);

            await step3.ExecuteAsync(jobRun, ctx, token);
        }

        try
        {
            var snapshot = await step1.ExecuteAsync(jobRun, ctx, ct);
            await step2.ExecuteAsync(jobRun, ctx, snapshot, ct);

            await FinalizeAsync(ctx.DryRun ? JobStatus.DryRun : JobStatus.Succeeded, ct);
        }
        catch (OperationCanceledException)
        {
            await FinalizeAsync(JobStatus.Partial, CancellationToken.None, "Cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(logger, ex.Message, ex);
            await FinalizeAsync(JobStatus.Failed, CancellationToken.None, ex.Message);
            throw;
        }
    }
}