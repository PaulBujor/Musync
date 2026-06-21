using System.ComponentModel.DataAnnotations;

namespace SpotifyTools.Options;

public abstract record ProviderOptionsBase
{
    [Required] public string ClientId { get; init; } = "";
    [Required] public string RedirectUri { get; init; } = "http://127.0.0.1:5000/callback";
    [Required] public string ApiBaseUrl { get; init; } = "";
    [Range(1, 10)] public int MaxRetries { get; init; } = 3;
}
