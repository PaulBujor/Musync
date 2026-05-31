using System.ComponentModel.DataAnnotations;

namespace SpotifyTools.Options;

public sealed record TidalOptions
{
    [Required] public string ClientId { get; init; } = "";
    [Required] public string ClientSecret { get; init; } = "";
}
