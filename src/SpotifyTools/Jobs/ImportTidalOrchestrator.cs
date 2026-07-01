using Microsoft.Extensions.Logging;
using SpotifyTools.Domain;
using SpotifyTools.Infrastructure.Persistence;

namespace SpotifyTools.Jobs;

public sealed class ImportTidalOrchestrator(
    AppDbContext db,
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

        async Task FinalizeAsync(string status, string? errorMessage = null)
        {
            jobRun.Status = status;
            jobRun.FinishedAt = DateTime.UtcNow;
            if (errorMessage is not null)
                jobRun.ErrorMessage = errorMessage;
            db.JobRuns.Update(jobRun);
            await db.SaveChangesAsync(ct);
            await step3.ExecuteAsync(jobRun);
        }

        try
        {
            var candidates = await step1.ExecuteAsync(jobRun, ct);
            await step2.ExecuteAsync(jobRun, candidates, ct);

            await FinalizeAsync("succeeded");
        }
        catch (OperationCanceledException)
        {
            await FinalizeAsync("partial", "Cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(logger, ex.Message, ex);
            await FinalizeAsync("failed", ex.Message);
            throw;
        }
    }
}
