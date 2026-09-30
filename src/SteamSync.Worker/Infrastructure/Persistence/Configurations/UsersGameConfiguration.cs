using SteamSync.Worker.Domain.Entities;
using SteamSync.Worker.Domain.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class UsersGameConfiguration : IEntityTypeConfiguration<UsersGame>
{
    public void Configure(EntityTypeBuilder<UsersGame> builder)
    {
        builder.ToTable("users_games");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.AchievementsPercentage)
            .HasPrecision(5, 2)
            .HasDefaultValue(0);

        builder.Property(e => e.PlaytimeMinutes)
            .HasDefaultValue(0);

        builder.Property(e => e.NeedsAchievementRefresh)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AchievementSyncUnavailable)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(e => e.AchievementsSyncedAt);

        builder.HasIndex(e => new { e.UserId, e.GameId }).IsUnique();
        builder.HasIndex(e => new { e.UserId, e.NeedsAchievementRefresh });
        builder.HasIndex(e => new { e.UserId, e.AchievementSyncUnavailable });
    }
}
