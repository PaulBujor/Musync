using System.ComponentModel.DataAnnotations;

namespace Musync.Options;

public sealed record DatabaseOptions
{
    public static readonly string[] Allowed = ["Sqlite", "Postgres"];

    [Required]
    [AllowedValues("Sqlite", "Postgres")]
    public string Provider { get; init; } = "Sqlite";
}
