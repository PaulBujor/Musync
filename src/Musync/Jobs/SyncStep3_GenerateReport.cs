using Microsoft.Extensions.Logging;
using Musync.Domain;

namespace Musync.Jobs;

public sealed class SyncStep3_GenerateReport(ILogger<SyncStep3_GenerateReport> logger)
{
    public Task ExecuteAsync(JobRun jobRun, CancellationToken ct)
    {
        var duration = jobRun.FinishedAt.HasValue
            ? jobRun.FinishedAt.Value - jobRun.StartedAt
            : TimeSpan.Zero;

        Log.SyncCompleteHeader(logger);
        Log.SyncDuration(logger, duration.ToString(@"hh\:mm\:ss"));
        Log.SyncStatus(logger, jobRun.Status);
        Log.TracksAdded(logger, jobRun.TracksAdded);
        Log.TracksRemoved(logger,
            jobRun.TracksRemovedLiked + jobRun.TracksRemovedManual,
            jobRun.TracksRemovedLiked,
            jobRun.TracksRemovedManual);
        Log.TracksSkipped(logger, jobRun.TracksSkipped);
        Log.NewAlbumsSeen(logger, jobRun.NewAlbumsEncountered);
        Log.QueueSize(logger, jobRun.QueueSizeAfter);
        Log.SyncCompleteFooter(logger);

        return Task.CompletedTask;
    }
}