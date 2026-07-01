using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Musync.Domain;

[Index(nameof(TidalTrackId), IsUnique = true)]
public sealed class TidalTrackMapping
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required, MaxLength(256)]
    public string TidalTrackId { get; set; } = "";

    [Required, MaxLength(256)]
    public string SpotifyTrackId { get; set; } = "";

    [MaxLength(50)]
    public string Isrc { get; set; } = "";

    public DateTime FirstMappedAt { get; set; }
}
