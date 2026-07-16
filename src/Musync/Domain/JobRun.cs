using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Musync.Domain;

public sealed class JobRun
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    [MaxLength(50)]
    public string Status { get; set; } = "running";

    [MaxLength(50)]
    public string? ProviderName { get; set; }

    [MaxLength(50)]
    public string? Command { get; set; }

    public bool DryRun { get; set; }

    public int? Limit { get; set; }

    public int TracksAdded { get; set; }
    public int TracksRemovedLiked { get; set; }
    public int TracksRemovedManual { get; set; }
    public int TracksSkipped { get; set; }
    public int NewAlbumsEncountered { get; set; }
    public int QueueSizeAfter { get; set; }

    [MaxLength(4000)]
    public string? Details { get; set; }

    [MaxLength(2000)]
    public string? ErrorMessage { get; set; }
}
