using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Tidal.Models;

[JsonSerializable(typeof(TidalFavoritesPage))]
public partial class TidalApiJsonContext : JsonSerializerContext;
