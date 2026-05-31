using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence.EntityConfigurations;

public sealed class ProcessedAlbumConfiguration : IEntityTypeConfiguration<ProcessedAlbum>
{
    public void Configure(EntityTypeBuilder<ProcessedAlbum> builder)
    {
        builder.ToTable("ProcessedAlbums");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SpotifyAlbumId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.AlbumName).HasMaxLength(500);
        builder.Property(x => x.ArtistName).HasMaxLength(500);
        builder.HasIndex(x => x.SpotifyAlbumId);
    }
}