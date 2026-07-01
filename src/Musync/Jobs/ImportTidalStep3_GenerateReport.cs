using Microsoft.Extensions.Logging;
using Musync.Domain;

namespace Musync.Jobs;

public sealed class ImportTidalStep3_GenerateReport(ILogger<ImportTidalStep3_GenerateReport> logger)
{
    public Task ExecuteAsync(JobRun jobRun)
    {
        var duration = jobRun.FinishedAt.HasValue
            ? jobRun.FinishedAt.Value - jobRun.StartedAt
            : TimeSpan.Zero;

        Log.TidalCompleteHeader(logger);
        Log.SyncDuration(logger, duration.ToString(@"hh\:mm\:ss"));
        Log.SyncStatus(logger, jobRun.Status);
        Log.TracksAdded(logger, jobRun.TracksAdded);
        Log.TracksSkipped(logger, jobRun.TracksSkipped);
        Log.TidalTracksMapped(logger, jobRun.NewAlbumsEncountered);
        Log.QueueSize(logger, jobRun.QueueSizeAfter);
        Log.SyncCompleteFooter(logger);

        return Task.CompletedTask;
    }
}
