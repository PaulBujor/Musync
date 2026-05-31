using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence.EntityConfigurations;

public sealed class TrackHistoryConfiguration : IEntityTypeConfiguration<TrackHistory>
{
    public void Configure(EntityTypeBuilder<TrackHistory> builder)
    {
        builder.ToTable("TrackHistories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).ValueGeneratedNever();
        builder.Property(x => x.SpotifyTrackId).HasMaxLength(256).IsRequired();
        builder.Property(x => x.TrackName).HasMaxLength(500);
        builder.Property(x => x.ArtistName).HasMaxLength(500);
        builder.Property(x => x.AlbumName).HasMaxLength(500);
        builder.Property(x => x.RemovalReason).HasMaxLength(50);
        builder.HasIndex(x => x.SpotifyTrackId);
        builder.HasIndex(x => x.RemovedAt);
    }
}