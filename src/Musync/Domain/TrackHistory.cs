using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Musync.Domain;

[Index(nameof(RemovedAt))]
[Index(nameof(JobRunId))]
public sealed class TrackHistory
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    public Guid JobRunId { get; set; }

    [Required, MaxLength(50)]
    public string Provider { get; set; } = "";

    [Required, MaxLength(256)]
    public string TrackId { get; set; } = "";

    [MaxLength(500)]
    public string TrackName { get; set; } = "";

    [MaxLength(500)]
    public string ArtistName { get; set; } = "";

    [MaxLength(500)]
    public string AlbumName { get; set; } = "";

    public DateTime AddedAt { get; set; }
    public DateTime? RemovedAt { get; set; }

    [MaxLength(50)]
    public string? RemovalReason { get; set; }
}
