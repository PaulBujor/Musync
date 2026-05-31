using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpotifyTools.Domain;

public sealed class RefreshTokens
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required, MaxLength(4000)]
    public string Token { get; set; } = "";

    [Required, MaxLength(50)]
    public string Provider { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
