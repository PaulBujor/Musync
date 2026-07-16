using Microsoft.Extensions.Logging;
using Musync.Domain;
using Musync.Infrastructure.Persistence;

namespace Musync.Jobs;

public sealed class ImportOrchestrator(
    AppDbContext db,
    ImportStep1_FetchAndMap step1,
    ImportStep2_AddToQueue step2,
    ImportStep3_GenerateReport step3,
    ILogger<ImportOrchestrator> logger)
{
    public async Task RunAsync(ImportRunContext ctx, CancellationToken ct)
    {
        var jobRun = new JobRun
        {
            Id = Guid.CreateVersion7(),
            StartedAt = DateTime.UtcNow,
            Status = "running",
            ProviderName = ctx.TargetProviderName,
            Command = $"import --source {ctx.SourceProviderName}",
            DryRun = ctx.DryRun,
            Limit = ctx.Limit
        };

        if (!ctx.DryRun)
        {
            db.JobRuns.Add(jobRun);
            await db.SaveChangesAsync(ct);
        }

        using var _ = logger.BeginScope(new { JobRunId = jobRun.Id.ToString() });
        Log.StartingJob(logger, $"import --source {ctx.SourceProviderName}", ctx.TargetProviderName, jobRun.Id.ToString());

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
            var candidates = await step1.ExecuteAsync(jobRun, ctx, ct);
            await step2.ExecuteAsync(jobRun, ctx, candidates, ct);

            await FinalizeAsync(ctx.DryRun ? "dry-run" : "succeeded", ct);
        }
        catch (OperationCanceledException)
        {
            await FinalizeAsync("partial", CancellationToken.None, "Cancelled by user");
            throw;
        }
        catch (Exception ex)
        {
            Log.JobFailed(logger, ex.Message, ex);
            await FinalizeAsync("failed", CancellationToken.None, ex.Message);
            throw;
        }
    }
}
