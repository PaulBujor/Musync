using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Infrastructure.Persistence;

namespace SpotifyTools.Jobs;

public sealed class ImportTidalOrchestrator(
    SpotifyDbContext db,
    ImportTidalStep1_FetchAndMap step1,
    ImportTidalStep2_AddToQueue step2,
    ImportTidalStep3_GenerateReport step3,
    ILogger<ImportTidalOrchestrator> logger)
{
    public async Task RunAsync(CancellationToken ct)
    {
        var jobRun = new JobRun
        {
            Id = Guid.CreateVersion7(),
            StartedAt = DateTime.UtcNow,
            Status = "running"
        };
        db.JobRuns.Add(jobRun);
        await db.SaveChangesAsync(ct);

        using var _ = logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.StartingJob(logger, jobRun.Id.ToString());

        try
        {
            var candidates = await step1.ExecuteAsync(jobRun, ct);
            await step2.ExecuteAsync(jobRun, candidates, ct);

            jobRun.Status = "succeeded";
            jobRun.FinishedAt = DateTime.UtcNow;
            db.JobRuns.Update(jobRun);
            await db.SaveChangesAsync(ct);

            await step3.ExecuteAsync(jobRun, ct);
        }
        catch (OperationCanceledException)
        {
            jobRun.Status = "partial";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = "Cancelled by user";
            db.JobRuns.Update(jobRun);
            await db.SaveChangesAsync(ct);
            await step3.ExecuteAsync(jobRun, ct);
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(logger, ex.Message, ex);
            jobRun.Status = "failed";
            jobRun.FinishedAt = DateTime.UtcNow;
            jobRun.ErrorMessage = ex.Message;
            db.JobRuns.Update(jobRun);
            await db.SaveChangesAsync(ct);
            await step3.ExecuteAsync(jobRun, ct);
            throw;
        }
    }
}
