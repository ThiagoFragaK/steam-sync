using SteamSync.Worker.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace SteamSync.Worker.Infrastructure.Persistence.Configurations;

public class UsersAchievementConfiguration : IEntityTypeConfiguration<UsersAchievement>
{
    public void Configure(EntityTypeBuilder<UsersAchievement> builder)
    {
        builder.ToTable("users_achievements");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.UnlockDate)
            .HasColumnName("unlock_date");

        builder.HasIndex(e => new { e.UserId, e.AchievementId }).IsUnique();
        builder.HasIndex(e => new { e.UserId, e.GameId });
        builder.HasIndex(e => new { e.UserId, e.UnlockDate })
            .HasFilter("unlock_date IS NOT NULL");
    }
}
