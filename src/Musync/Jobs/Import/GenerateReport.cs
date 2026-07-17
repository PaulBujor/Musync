using Microsoft.Extensions.Logging;
using Musync.Domain;

namespace Musync.Jobs.Import;

public sealed class GenerateReport(ILogger<GenerateReport> logger)
{
    public Task ExecuteAsync(JobRun jobRun, ImportRunContext ctx, CancellationToken ct)
    {
        var duration = jobRun.FinishedAt.HasValue
            ? jobRun.FinishedAt.Value - jobRun.StartedAt
            : TimeSpan.Zero;

        if (ctx.DryRun)
            Log.DryRunActive(logger);

        Log.ImportCompleteHeader(logger);
        Log.SyncDuration(logger, duration.ToString(@"hh\:mm\:ss"));
        Log.SyncStatus(logger, jobRun.Status);
        Log.TracksAdded(logger, jobRun.TracksAdded);
        Log.TracksSkipped(logger, jobRun.TracksSkipped);
        Log.TracksMapped(logger, jobRun.TracksMapped);
        Log.QueueSize(logger, jobRun.QueueSizeAfter);

        if (ctx.Limit.HasValue)
            Log.LimitApplied(logger, ctx.Limit.Value);

        Log.SyncCompleteFooter(logger);

        return Task.CompletedTask;
    }
}