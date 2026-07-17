using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Musync.Domain;

[Index(nameof(Provider), nameof(UpdatedAt))]
public sealed class RefreshToken
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.None)]
    public Guid Id { get; set; }

    /// <summary>Encrypted at rest via .NET Data Protection (see AppDbContext); the plaintext is only visible in memory.</summary>
    [Required]
    [MaxLength(4000)]
    public string Token { get; set; } = "";

    [Required] [MaxLength(50)] public string Provider { get; set; } = "";

    public DateTime UpdatedAt { get; set; }
}