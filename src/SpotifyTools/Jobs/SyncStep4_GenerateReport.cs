using Microsoft.Extensions.Logging;

namespace SpotifyTools.Jobs;

public sealed class SyncStep4_GenerateReport
{
    private readonly ILogger<SyncStep4_GenerateReport> _logger;

    public SyncStep4_GenerateReport(ILogger<SyncStep4_GenerateReport> logger)
    {
        _logger = logger;
    }

    public Task ExecuteAsync(Domain.JobRun jobRun, CancellationToken ct)
    {
        var duration = jobRun.FinishedAt.HasValue
            ? jobRun.FinishedAt.Value - jobRun.StartedAt
            : TimeSpan.Zero;

        Log.SyncCompleteHeader(_logger);
        Log.SyncDuration(_logger, duration.ToString(@"hh\:mm\:ss"));
        Log.SyncStatus(_logger, jobRun.Status);
        Log.TracksAdded(_logger, jobRun.TracksAdded);
        Log.TracksRemoved(_logger,
            jobRun.TracksRemovedLiked + jobRun.TracksRemovedManual,
            jobRun.TracksRemovedLiked,
            jobRun.TracksRemovedManual);
        Log.TracksSkipped(_logger, jobRun.TracksSkipped);
        Log.NewAlbumsSeen(_logger, jobRun.NewAlbumsEncountered);
        Log.QueueSize(_logger, jobRun.QueueSizeAfter);
        Log.SyncCompleteFooter(_logger);

        return Task.CompletedTask;
    }
}
