using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Musync.Domain;

[Index(nameof(SpotifyAlbumId))]
public sealed class ProcessedAlbum
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required, MaxLength(256)]
    public string SpotifyAlbumId { get; set; } = "";

    [MaxLength(500)]
    public string AlbumName { get; set; } = "";

    [MaxLength(500)]
    public string ArtistName { get; set; } = "";

    public DateTime FirstProcessedAt { get; set; }
    public DateTime LastSeenAt { get; set; }
}