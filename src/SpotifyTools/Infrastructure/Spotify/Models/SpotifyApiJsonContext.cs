using System.Text.Json.Serialization;

namespace SpotifyTools.Infrastructure.Spotify.Models;

[JsonSerializable(typeof(PagedResponse<SavedAlbumItem>))]
[JsonSerializable(typeof(PagedResponse<SpotifyTrackDto>))]
[JsonSerializable(typeof(PagedResponse<PlaylistTrackItem>))]
[JsonSerializable(typeof(PagedResponse<LikedTrackItem>))]
public partial class SpotifyApiJsonContext : JsonSerializerContext;
