using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SpotifyTools.Domain;

namespace SpotifyTools.Infrastructure.Persistence.EntityConfigurations;

public sealed class AppSettingConfiguration : IEntityTypeConfiguration<AppSetting>
{
    public void Configure(EntityTypeBuilder<AppSetting> builder)
    {
        builder.ToTable("AppSettings");
        builder.HasKey(x => x.Key);
        builder.Property(x => x.Key).HasMaxLength(256);
        builder.Property(x => x.Value).HasMaxLength(4000);
    }
}
