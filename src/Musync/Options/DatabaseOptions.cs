using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed class DatabaseOptions
{
    [Required]
    public string Provider { get; set; } = "Sqlite";
}
