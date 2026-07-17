using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

[JsonSerializable(typeof(TidalCollectionResponse))]
public partial class TidalApiJsonContext : JsonSerializerContext;
