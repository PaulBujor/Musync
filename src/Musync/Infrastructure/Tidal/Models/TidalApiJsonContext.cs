using System.Text.Json.Serialization;

namespace Musync.Infrastructure.Tidal.Models;

[JsonSerializable(typeof(TidalCollectionResponse))]
// Serialized (not just read) as the JSON:API body for playlist relationship add/remove writes.
[JsonSerializable(typeof(TidalRelationshipData))]
public partial class TidalApiJsonContext : JsonSerializerContext;
