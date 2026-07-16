using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

[JsonSerializable(typeof(TidalCollectionPage))]
[JsonSerializable(typeof(TidalArtistsPage))]
[JsonSerializable(typeof(TidalAlbumsPage))]
[JsonSerializable(typeof(TidalUserMeResponse))]
public partial class TidalApiJsonContext : JsonSerializerContext;
