using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Musync.Domain;

public sealed class JobRun
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [MaxLength(50)] public string Status { get; set; } = JobStatus.Running;

    [MaxLength(50)] public string? ProviderName { get; set; }

    [MaxLength(50)] public string? Command { get; set; }

    public bool DryRun { get; set; }

    public int? Limit { get; set; }

    public int TracksAdded { get; set; }
    public int TracksRemovedLiked { get; set; }
    public int TracksRemovedManual { get; set; }
    public int TracksSkipped { get; set; }
    public int NewAlbumsEncountered { get; set; }

    /// <summary>Tracks matched to a target-provider id during import (queue-albums leaves this 0).</summary>
    public int TracksMapped { get; set; }

    public int QueueSizeAfter { get; set; }

    [MaxLength(2000)] public string? ErrorMessage { get; set; }
}