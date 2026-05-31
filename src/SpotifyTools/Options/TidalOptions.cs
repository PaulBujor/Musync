using System.ComponentModel.DataAnnotations;

namespace SpotifyTools.Options;

public sealed record TidalOptions
{
    [Required] public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    [Required] public string RedirectUri { get; init; } = "http://127.0.0.1:5000/callback";
}
