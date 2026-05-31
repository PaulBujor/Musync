using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace SpotifyTools.Domain;

[Table("refresh_tokens")]
public sealed class RefreshToken
{
    [Key, DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    [Required, MaxLength(4000)]
    public string Token { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}
