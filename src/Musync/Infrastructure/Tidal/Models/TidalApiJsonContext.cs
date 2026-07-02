using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

[JsonSerializable(typeof(TidalFavoritesPage))]
public partial class TidalApiJsonContext : JsonSerializerContext;
