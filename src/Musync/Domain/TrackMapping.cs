using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Musync.Domain;

[Index(nameof(SourceProvider), nameof(SourceTrackId), IsUnique = true)]
public sealed class TrackMapping
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required] [MaxLength(50)] public string SourceProvider { get; set; } = "";

    [Required] [MaxLength(256)] public string SourceTrackId { get; set; } = "";

    [Required] [MaxLength(50)] public string TargetProvider { get; set; } = "";

    [Required] [MaxLength(256)] public string TargetTrackId { get; set; } = "";

    [MaxLength(50)] public string Isrc { get; set; } = "";

    public DateTime FirstMappedAt { get; set; }
}