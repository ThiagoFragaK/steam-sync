using SteamSync.Worker.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Data.Configurations;

public class AchievementConfiguration : IEntityTypeConfiguration<Achievement>
{
    public void Configure(EntityTypeBuilder<Achievement> builder)
    {
        builder.ToTable("achievements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.ApiName)
            .HasMaxLength(500);

        builder.Property(e => e.Name)
            .IsRequired()
            .HasMaxLength(500);

        builder.Property(e => e.Description)
            .HasColumnName("desc")
            .HasMaxLength(1000);

        builder.Property(e => e.ImageUrlLock)
            .HasMaxLength(500);

        builder.Property(e => e.ImageUrlUnlock)
            .HasMaxLength(500);

        builder.Property(e => e.GlobalPercentage)
            .HasPrecision(5, 2);

        builder.HasIndex(e => new { e.GameId, e.ApiName })
            .IsUnique()
            .HasFilter("\"ApiName\" IS NOT NULL AND \"ApiName\" <> ''");

        builder.HasMany(e => e.UsersAchievements)
            .WithOne(ua => ua.Achievement)
            .HasForeignKey(ua => ua.AchievementId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
