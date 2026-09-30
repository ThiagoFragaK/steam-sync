using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Email)
            .IsRequired()
            .HasMaxLength(256);

        builder.Property(e => e.SteamId)
            .HasMaxLength(64);

        builder.Property(e => e.Password)
            .IsRequired()
            .HasMaxLength(512);

        builder.Property(e => e.Role)
            .IsRequired()
            .HasMaxLength(32)
            .HasDefaultValue("user");

        builder.Property(e => e.Status)
            .IsRequired()
            .HasDefaultValue(1);

        builder.Property(e => e.IsEmailVerified)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.TokenVersion)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.LastLogin);

        builder.Property(e => e.Playtime2WeeksMinutes)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.AvgPercentage)
            .HasColumnName("avg_percentage")
            .HasPrecision(5, 2)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.AchievementSyncCoverage)
            .HasColumnName("achievement_sync_coverage")
            .HasPrecision(5, 2)
            .IsRequired()
            .HasDefaultValue(0);

        builder.Property(e => e.SteamLibraryPublic)
            .HasColumnName("steam_library_public")
            .IsRequired()
            .HasDefaultValue(true);

        builder.HasIndex(e => e.Email).IsUnique();
        builder.HasIndex(e => e.SteamId)
            .IsUnique()
            .HasFilter("\"SteamId\" IS NOT NULL");

        builder.HasMany(e => e.UsersGames)
            .WithOne(ug => ug.User)
            .HasForeignKey(ug => ug.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.UsersAchievements)
            .WithOne(ua => ua.User)
            .HasForeignKey(ua => ua.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(e => e.Goals)
            .WithOne(g => g.User)
            .HasForeignKey(g => g.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
